using System;
using System.Collections;
using System.Collections.Generic;
using GuildTactics.HexGrid;
using GuildTactics.Combat;
using GuildTactics.Abilities;
using UnityEngine;
using GridModel = GuildTactics.HexGrid.HexGrid;

namespace GuildTactics.Units
{
    /// <summary>Coordinates player unit selection, legal movement and its placeholder animation.</summary>
    public sealed class PlayerUnitController : MonoBehaviour
    {
        private static readonly HexCoordinates[] SpawnCoordinates =
        {
            new HexCoordinates(1, 1), new HexCoordinates(1, 2),
            new HexCoordinates(2, 1), new HexCoordinates(2, 2)
        };
        private static readonly Color[] HeroColors =
        {
            new Color(0.72f, 0.25f, 0.22f), new Color(0.55f, 0.38f, 0.72f),
            new Color(0.23f, 0.58f, 0.34f), new Color(0.22f, 0.45f, 0.76f)
        };

        private readonly List<UnitRuntimeState> units = new List<UnitRuntimeState>();
        private readonly List<UnitRuntimeState> enemies = new List<UnitRuntimeState>();
        private readonly Dictionary<string, UnitView> views = new Dictionary<string, UnitView>(StringComparer.Ordinal);
        private GridModel grid;
        private HexLayout layout;
        private HexGridView gridView;
        private HexGridInteraction interaction;
        private float secondsPerStep;
        private Coroutine movementRoutine;
        private Coroutine actionRoutine;
        private CombatSystem combat;
        private AbilitySystem abilities;
        public AbilityDefinition SelectedAbility { get; private set; }
        public bool IsTargetingAttack { get; private set; }
        public string ActionHint { get; private set; } = "Choose an action or move to a green hex.";
        public AbilityResult LastAbility { get; private set; }
        private CombatText combatText;
        private bool battleEnabled;
        private bool enemyMoved;
        public BattleOutcome Outcome { get; private set; } = BattleOutcome.Ongoing;
        public bool CanPlayerAct => isActiveAndEnabled && Outcome == BattleOutcome.Ongoing &&
            Turns != null && Turns.CanSelectAction(SelectedUnit) && SelectedUnit.Team == UnitTeam.Player &&
            (!battleEnabled || BattleRules.Evaluate(Turns.Order) == BattleOutcome.Ongoing);
        private const float WaitActionSeconds = 0.35f;
        public TurnManager Turns { get; private set; }
        public IReadOnlyList<UnitRuntimeState> Units { get; private set; }
        public IReadOnlyList<UnitRuntimeState> Enemies { get; private set; }
        public AttackResult LastAttack { get; private set; }
        public UnitRuntimeState SelectedUnit { get; private set; }
        public HexMovementRange CurrentRange { get; private set; }
        public bool IsMoving => movementRoutine != null;

        public void Initialize(GridModel model, HexLayout hexLayout, HexGridView view,
            HexGridInteraction gridInteraction, float moveSecondsPerStep, IDice dice = null, CombatText feedback = null,
            bool enableBattle = true)
        {
            if (grid != null) throw new InvalidOperationException("Unit controller is already initialized.");
            if (float.IsNaN(moveSecondsPerStep) || float.IsInfinity(moveSecondsPerStep) || moveSecondsPerStep < 0)
                throw new ArgumentOutOfRangeException(nameof(moveSecondsPerStep));
            grid = model ?? throw new ArgumentNullException(nameof(model));
            layout = hexLayout ?? throw new ArgumentNullException(nameof(hexLayout));
            gridView = view != null ? view : throw new ArgumentNullException(nameof(view));
            interaction = gridInteraction != null ? gridInteraction : throw new ArgumentNullException(nameof(gridInteraction));
            secondsPerStep = moveSecondsPerStep;
            combatText = feedback;
            battleEnabled = enableBattle;

            var definitions = HeroDefinitions.Defaults;
            if (definitions.Count != SpawnCoordinates.Length)
                throw new InvalidOperationException("The prototype requires exactly four hero definitions.");
            try
            {
                for (int index = 0; index < definitions.Count; index++)
                    Spawn("hero-" + definitions[index].Id, definitions[index], SpawnCoordinates[index], HeroColors[index]);
                // The isolated WP-06 regression fixture retains its stationary target.
                Spawn("enemy-practice", new UnitDefinition("practice", "Crypt Sentinel", enableBattle ? 3 : 0, 3,
                    maxHealth: 18, defense: 12), new HexCoordinates(1, 3),
                    new Color(0.82f, 0.56f, 0.16f), UnitTeam.Enemy);
                if (enableBattle)
                {
                    var guard = new UnitDefinition("crypt-guard", "Crypt Guard", 3, 2,
                        maxHealth: 16, attack: 3, damageDie: 4, damageBonus: 1);
                    Spawn("enemy-guard-1", guard, new HexCoordinates(7, 4),
                        new Color(0.82f, 0.56f, 0.16f), UnitTeam.Enemy);
                    Spawn("enemy-guard-2", guard, new HexCoordinates(5, 7),
                        new Color(0.82f, 0.56f, 0.16f), UnitTeam.Enemy);
                }
            }
            catch
            {
                foreach (var unit in units) grid.TryVacate(unit.Position, unit.InstanceId);
                foreach (var enemy in enemies) grid.TryVacate(enemy.Position, enemy.InstanceId);
                foreach (var unitView in views.Values) Destroy(unitView.gameObject);
                units.Clear();
                enemies.Clear();
                views.Clear();
                throw;
            }

            Units = units.AsReadOnly();
            Enemies = enemies.AsReadOnly();
            var participants = new List<UnitRuntimeState>(units);
            if (enableBattle) participants.AddRange(enemies);
            Turns = new TurnManager(grid, participants);
            var battleDice = dice ?? new SeededDice(SeededDice.DefaultSeed);
            combat = new CombatSystem(grid, Turns, battleDice);
            var abilityParticipants = new List<UnitRuntimeState>(units);
            abilityParticipants.AddRange(enemies);
            abilities = new AbilitySystem(grid, Turns, battleDice, abilityParticipants);
            StartNextTurn();
            interaction.CellClicked += HandleCellClicked;
        }

        public bool TrySelectUnit(HexCoordinates coordinate)
        {
            if (!CanPlayerAct) return false;
            UnitRuntimeState found = null;
            foreach (var unit in units)
                if (unit.Position == coordinate) { found = unit; break; }
            if (!Turns.CanSelectAction(found)) return false;
            SelectedUnit = found;
            RefreshSelection();
            return true;
        }

        public void SetAnimationSecondsPerStep(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            if (IsMoving) throw new InvalidOperationException("Cannot change animation speed during movement.");
            secondsPerStep = value;
        }

        public bool TryMoveSelected(HexCoordinates destination)
        {
            if (!CanPlayerAct) return false;
            return BeginMovement(destination);
        }

        private bool BeginMovement(HexCoordinates destination)
        {
            // Recalculate immediately before commit so stale highlights never grant permission.
            if (!Turns.TryBeginMovement(SelectedUnit, destination, out var path))
            {
                RefreshSelection();
                return false;
            }

            CurrentRange = null;
            gridView.SetReachableCells(null);
            interaction.SetSelected(SelectedUnit.Position);
            if (secondsPerStep <= 0)
            {
                views[SelectedUnit.InstanceId].SnapTo(layout.ToWorld(SelectedUnit.Position));
                Turns.TryCompleteMovement(SelectedUnit);
                ShowTrapFeedback();
                RefreshSelection();
            }
            else movementRoutine = StartCoroutine(AnimateMovement(SelectedUnit, path));
            return true;
        }

        private void Spawn(string instanceId, UnitDefinition definition, HexCoordinates coordinate, Color color,
            UnitTeam team = UnitTeam.Player)
        {
            if (!UnitRuntimeState.TrySpawn(grid, instanceId, definition, coordinate, out var unit, team))
                throw new InvalidOperationException("Cannot spawn " + instanceId + " at " + coordinate + ".");
            GameObject unitObject = null;
            try
            {
                unitObject = new GameObject(definition.DisplayName + " (" + instanceId + ")");
                unitObject.transform.SetParent(transform, false);
                var unitView = unitObject.AddComponent<UnitView>();
                unitView.Initialize(unit, layout, color, interaction.GridCamera);
                if (team == UnitTeam.Player) units.Add(unit);
                else enemies.Add(unit);
                views.Add(instanceId, unitView);
            }
            catch
            {
                grid.TryVacate(coordinate, instanceId);
                if (unitObject != null) Destroy(unitObject);
                throw;
            }
        }

        private void HandleCellClicked(HexCoordinates coordinate)
        {
            if (!CanPlayerAct) return;
            if (SelectedAbility != null) { TryUseSelectedAbility(coordinate); return; }
            if (IsTargetingAttack)
            {
                if (!TryAttackSelected(coordinate)) ActionHint = "Choose an enemy within basic attack range.";
                return;
            }
            if (TrySelectUnit(coordinate)) return;
            if (TryAttackSelected(coordinate)) return;
            if (!TryMoveSelected(coordinate) && SelectedUnit != null)
                interaction.SetSelected(SelectedUnit.Position);
        }

        private void RefreshSelection()
        {
            foreach (var pair in views)
            {
                pair.Value.gameObject.SetActive(pair.Value.State.IsAlive);
                pair.Value.SetSelected(SelectedUnit != null && pair.Key == SelectedUnit.InstanceId);
            }
            interaction.SetSelected(SelectedUnit?.Position);
            CurrentRange = !CanPlayerAct ? null :
                HexPathfinder.FindReachable(grid, SelectedUnit.Position, Turns.RemainingMovement);
            gridView.SetReachableCells(CurrentRange == null ? null : CurrentRange.Costs.Keys);
            var targets = new List<HexCoordinates>();
            if (CanPlayerAct && SelectedAbility != null)
                foreach (var cell in grid.Cells)
                    if (abilities.CanUse(SelectedUnit, SelectedAbility, cell.Coordinates, out _)) targets.Add(cell.Coordinates);
            if (CanPlayerAct && IsTargetingAttack)
                foreach (var enemy in enemies)
                    if (CanAttack(enemy)) targets.Add(enemy.Position);
            if (SelectedAbility != null || IsTargetingAttack) gridView.SetReachableCells(null);
            gridView.SetTargetCells(targets);
            var traps = new List<HexCoordinates>();
            foreach (var trap in Turns.Traps.Traps) traps.Add(trap.Position);
            gridView.SetTrapCells(traps);
        }

        public bool SelectAbility(AbilityDefinition ability)
        {
            if (!CanPlayerAct || !Turns.ActionAvailable || ability == null) return false;
            bool known = false;
            foreach (var item in SelectedUnit.Definition.Abilities) if (ReferenceEquals(item, ability)) known = true;
            if (!known) return false;
            SelectedAbility = ability;
            IsTargetingAttack = false;
            ActionHint = ability.Description;
            RefreshSelection();
            return true;
        }

        public bool SelectBasicAttack()
        {
            if (!CanPlayerAct || !Turns.ActionAvailable) return false;
            SelectedAbility = null;
            IsTargetingAttack = true;
            ActionHint = $"Basic attack: range {SelectedUnit.Definition.AttackRange}. Choose a highlighted enemy.";
            RefreshSelection();
            return true;
        }

        public void CancelTargeting()
        {
            SelectedAbility = null;
            IsTargetingAttack = false;
            ActionHint = "Choose an action or move to a green hex.";
            if (Turns != null) RefreshSelection();
        }

        public bool TryUseSelectedAbility(HexCoordinates target)
        {
            if (!CanPlayerAct || SelectedAbility == null) return false;
            if (!abilities.TryUse(SelectedUnit, SelectedAbility, target, out var result, out var reason))
            { ActionHint = reason; RefreshSelection(); return false; }
            LastAbility = result;
            // Relocation is already committed; presentation never applies an ability twice.
            foreach (var view in views.Values) view.SnapTo(layout.ToWorld(view.State.Position));
            CancelTargeting();
            if (combatText != null) combatText.Show(result, layout.ToWorld(result.Position));
            actionRoutine = StartCoroutine(AnimateAction(SelectedUnit, CombatText.DisplaySeconds));
            return true;
        }

        private void ShowTrapFeedback()
        {
            if (combatText == null || Turns.LastTrapHits.Count == 0) return;
            var text = new System.Text.StringBuilder();
            foreach (var hit in Turns.LastTrapHits)
                text.Append("TRAP ").Append(hit.Position).Append(": -").Append(hit.Damage)
                    .Append(hit.Killed ? " HP / DEAD\n" : " HP\n");
            combatText.ShowMessage(text.ToString(), layout.ToWorld(SelectedUnit.Position));
        }

        private IEnumerator AnimateMovement(UnitRuntimeState movingUnit, IReadOnlyList<HexCoordinates> path)
        {
            var unitView = views[movingUnit.InstanceId];
            for (int index = 1; index < path.Count; index++)
            {
                Vector3 from = layout.ToWorld(path[index - 1]);
                Vector3 to = layout.ToWorld(path[index]);
                float elapsed = 0;
                while (elapsed < secondsPerStep)
                {
                    elapsed += Time.unscaledDeltaTime;
                    unitView.SetWorldPosition(Vector3.Lerp(from, to, Mathf.Clamp01(elapsed / secondsPerStep)));
                    yield return null;
                }
                unitView.SnapTo(to);
            }
            movementRoutine = null;
            Turns.TryCompleteMovement(movingUnit);
            ShowTrapFeedback();
            RefreshSelection();
        }

        public bool TryEndTurn()
        {
            if (!CanPlayerAct || !Turns.TryEndTurn(SelectedUnit)) return false;
            RefreshSelection();
            return true;
        }

        public bool CanAttack(UnitRuntimeState target) => CanPlayerAct && combat != null &&
            combat.CanAttack(SelectedUnit, target);

        public bool TryAttackSelected(HexCoordinates coordinate)
        {
            if (!CanPlayerAct || combat == null) return false;
            UnitRuntimeState target = enemies.Find(unit => unit.IsAlive && unit.Position == coordinate);
            return BeginAttack(target);
        }

        private bool BeginAttack(UnitRuntimeState target)
        {
            if (!combat.TryAttack(SelectedUnit, target, out var result)) return false;
            LastAttack = result;
            CancelTargeting();
            if (combatText != null) combatText.Show(result, layout.ToWorld(result.TargetPosition));
            actionRoutine = StartCoroutine(AnimateAction(SelectedUnit, CombatText.DisplaySeconds));
            return true;
        }

        // Retained as an explicit way to spend an unused action and for turn-loop regression checks.
        public bool TryWaitAction()
        {
            if (!CanPlayerAct || !Turns.TryBeginAction(SelectedUnit)) return false;
            RefreshSelection();
            actionRoutine = StartCoroutine(AnimateAction(SelectedUnit, WaitActionSeconds));
            return true;
        }

        private IEnumerator AnimateAction(UnitRuntimeState unit, float duration)
        {
            float elapsed = 0;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            actionRoutine = null;
            Turns.TryCompleteAction(unit);
            RefreshSelection();
        }

        private void Update()
        {
            if (Turns == null || Outcome != BattleOutcome.Ongoing ||
                Turns.State == TurnState.Moving || Turns.State == TurnState.ResolvingAction) return;
            if (battleEnabled)
            {
                Outcome = BattleRules.Evaluate(Turns.Order);
                if (Outcome != BattleOutcome.Ongoing)
                {
                    SelectedUnit = null;
                    RefreshSelection();
                    return;
                }
            }
            if (Turns.State == TurnState.TurnComplete) { StartNextTurn(); return; }
            if (SelectedUnit != null && !SelectedUnit.IsAlive)
            { Turns.TryEndTurn(SelectedUnit); RefreshSelection(); return; }
            if (battleEnabled && SelectedUnit != null && SelectedUnit.Team == UnitTeam.Enemy)
                AdvanceEnemyTurn();
        }

        private void AdvanceEnemyTurn()
        {
            var target = MeleeBrain.FindTarget(combat, SelectedUnit, Units);
            if (target != null && BeginAttack(target)) return;
            if (!enemyMoved && Turns.ActionAvailable)
            {
                enemyMoved = true;
                var destination = MeleeBrain.ChooseDestination(grid, SelectedUnit, Units, Turns.RemainingMovement);
                if (destination != SelectedUnit.Position && BeginMovement(destination)) return;
            }
            // One movement plan and at most one attack, including unreachable/immobile enemies.
            Turns.TryEndTurn(SelectedUnit);
            RefreshSelection();
        }

        private void StartNextTurn()
        {
            if (!Turns.TryStartNextTurn()) return;
            SelectedUnit = Turns.ActiveUnit;
            enemyMoved = false;
            CancelTargeting();
        }

        private void OnDisable()
        {
            if (interaction != null) interaction.CellClicked -= HandleCellClicked;
            if (movementRoutine != null)
            {
                StopCoroutine(movementRoutine);
                movementRoutine = null;
                if (SelectedUnit != null && views.TryGetValue(SelectedUnit.InstanceId, out var view))
                    view.SnapTo(layout.ToWorld(SelectedUnit.Position));
                Turns.TryCompleteMovement(SelectedUnit);
            }
            if (actionRoutine != null)
            {
                StopCoroutine(actionRoutine);
                actionRoutine = null;
                Turns.TryCompleteAction(SelectedUnit);
            }
            CurrentRange = null;
            SelectedAbility = null;
            IsTargetingAttack = false;
            if (gridView != null)
            {
                gridView.SetReachableCells(null);
                gridView.SetTargetCells(null);
            }
        }

        private void OnEnable()
        {
            if (interaction != null) interaction.CellClicked += HandleCellClicked;
            if (Turns != null) RefreshSelection();
        }

        private void OnGUI()
        {
            if (Turns == null || SelectedUnit == null) return;
            string status = $"{SelectedUnit.Definition.DisplayName} | HP {SelectedUnit.CurrentHealth}/{SelectedUnit.Definition.MaxHealth} | DEF {SelectedUnit.Defense} | Move {Turns.RemainingMovement} | Action {(Turns.ActionAvailable ? "ready" : "used")} | {Turns.State}";
            GUI.Label(new Rect(24, 88, 760, 24), status);
        }
    }
}
