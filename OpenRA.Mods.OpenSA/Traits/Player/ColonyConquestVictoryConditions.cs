using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Network;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.SA.Traits
{
	public class ColonyConquestVictoryConditionsInfo : TraitInfo, Requires<MissionObjectivesInfo>
	{
		[Desc("Delay for the end game notification in milliseconds.")]
		public readonly int NotificationDelay = 1500;

		[Translate]
		[Desc("Description of the objective.")]
		public readonly string Objective = "Conquer all colonies!";

		[Desc("Disable the win/loss messages and audio notifications?")]
		public readonly bool SuppressNotifications = false;

		public override object Create(ActorInitializer init) { return new ColonyConquestVictoryConditions(init.Self, this); }
	}

	public class ColonyConquestVictoryConditions : ITick, INotifyWinStateChanged, INotifyTimeLimit
	{
		readonly ColonyConquestVictoryConditionsInfo info;
		readonly MissionObjectives missionObjectives;
		readonly IEnumerable<Actor> colonies;

		OpenRA.Player[] otherPlayers;
		int objectiveID = -1;

		public ColonyConquestVictoryConditions(Actor self, ColonyConquestVictoryConditionsInfo info)
		{
			this.info = info;
			missionObjectives = self.Trait<MissionObjectives>();
			colonies = self.World.ActorsHavingTrait<Colony>();
		}

		void ITick.Tick(Actor self)
		{
			if (self.Owner.WinState != WinState.Undefined || self.Owner.NonCombatant)
				return;

			if (objectiveID < 0)
				objectiveID = missionObjectives.Add(self.Owner, info.Objective, "Primary", inhibitAnnouncement: true);

			// Players, NonCombatants, and IsAlliedWith are all fixed once the game starts, so we can cache the result.
			if (otherPlayers == null)
				otherPlayers = self.World.Players.Where(p => !p.NonCombatant && !p.IsAlliedWith(self.Owner)).ToArray();

			if (colonies.All(c => c.Owner == self.Owner || c.Owner.IsAlliedWith(self.Owner)))
			{
				missionObjectives.MarkCompleted(self.Owner, objectiveID);

				foreach (var enemy in otherPlayers)
					missionObjectives.MarkFailed(enemy, objectiveID);
			}
		}

		void INotifyTimeLimit.NotifyTimerExpired(Actor self)
		{
			if (objectiveID < 0)
				return;

			var myTeam = self.World.LobbyInfo.ClientWithIndex(self.Owner.ClientIndex).Team;
			var teams = self.World.Players.Where(p => !p.NonCombatant && p.Playable)
				.Select(p => new Pair<OpenRA.Player, PlayerStatistics>(p, p.PlayerActor.TraitOrDefault<PlayerStatistics>()))
				.OrderByDescending(p => p.Second != null ? p.Second.Experience : 0)
				.GroupBy(p => (self.World.LobbyInfo.ClientWithIndex(p.First.ClientIndex) ?? new Session.Client()).Team)
				.OrderByDescending(g => g.Sum(gg => gg.Second != null ? gg.Second.Experience : 0));

			if (teams.First().Key == myTeam && (myTeam != 0 || teams.First().First().First == self.Owner))
			{
				missionObjectives.MarkCompleted(self.Owner, objectiveID);
				return;
			}

			missionObjectives.MarkFailed(self.Owner, objectiveID);
		}

		void INotifyWinStateChanged.OnPlayerLost(OpenRA.Player player)
		{
			foreach (var a in player.World.ActorsWithTrait<INotifyOwnerLost>().Where(a => a.Actor.Owner == player))
				a.Trait.OnOwnerLost(a.Actor);

			if (info.SuppressNotifications)
				return;

			Game.AddSystemLine(player.PlayerName + " is defeated.");
			Game.RunAfterDelay(info.NotificationDelay, () =>
			{
				if (Game.IsCurrentWorld(player.World) && player == player.World.LocalPlayer)
					Game.Sound.PlayNotification(player.World.Map.Rules, player, "Speech", missionObjectives.Info.LoseNotification, player.Faction.InternalName);
			});
		}

		void INotifyWinStateChanged.OnPlayerWon(OpenRA.Player player)
		{
			if (info.SuppressNotifications)
				return;

			Game.AddSystemLine(player.PlayerName + " is victorious.");
			Game.RunAfterDelay(info.NotificationDelay, () =>
			{
				if (Game.IsCurrentWorld(player.World) && player == player.World.LocalPlayer)
					Game.Sound.PlayNotification(player.World.Map.Rules, player, "Speech", missionObjectives.Info.WinNotification, player.Faction.InternalName);
			});
		}
	}
}
