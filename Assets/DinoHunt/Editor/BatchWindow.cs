using System.Diagnostics;
using System.IO;
using DinoHunt.Arena;
using DinoHunt.Batch;
using DinoHunt.Core;
using UnityEditor;
using UnityEngine;

namespace DinoHunt.Editor
{
    /// <summary>
    /// Editor window for running headless batches. Deliberately not Play mode and not
    /// -batchmode: it runs the simulation directly, so batches work while the editor is open
    /// and while you are doing something else — no scene, no rendering, no domain reload.
    /// </summary>
    public sealed class BatchWindow : EditorWindow
    {
        private MatchConfig _config = new MatchConfig();
        private ArenaLayout _layout = new ArenaLayout();
        private int _matches = 50;
        private ulong _firstSeed = 1;
        private bool _writeCsv = true;

        private string _summary = "";
        private Vector2 _scroll;
        private string _lastCsvPath;

        /// <summary>Previous batch, kept so each run reports its delta against the one before it.</summary>
        private System.Collections.Generic.List<MatchResult> _baseline;

        [MenuItem("DinoHunt/Batch Runner")]
        public static void Open() => GetWindow<BatchWindow>("DinoHunt Batch");

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Headless batch", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Runs matches with no rendering, using analytic line-of-sight and grid pathfinding " +
                "instead of Unity physics and NavMesh. Same AI, combat, eggs, raptors, and event log as a played match.",
                MessageType.Info);

            _matches = EditorGUILayout.IntField("Matches", Mathf.Max(1, _matches));
            _firstSeed = (ulong)Mathf.Max(0, EditorGUILayout.IntField("First seed", (int)_firstSeed));
            _writeCsv = EditorGUILayout.Toggle("Write per-agent CSV", _writeCsv);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Match config", EditorStyles.boldLabel);
            _config.teamSize = EditorGUILayout.IntField("Team size", _config.teamSize);
            _config.raptorCount = EditorGUILayout.IntField("Raptors", _config.raptorCount);
            _config.eggCount = EditorGUILayout.IntField("Eggs", _config.eggCount);
            _config.matchTimerSeconds = EditorGUILayout.FloatField("Match timer (s)", _config.matchTimerSeconds);
            _config.perAgentPersonalities = EditorGUILayout.Toggle("Per-agent personalities", _config.perAgentPersonalities);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Radio (research Q2 variables)", EditorStyles.boldLabel);
            _config.radioEnabled = EditorGUILayout.Toggle("Radio enabled", _config.radioEnabled);
            using (new EditorGUI.DisabledScope(!_config.radioEnabled))
            {
                _config.calloutEnemy = EditorGUILayout.Toggle("  vocab: enemy", _config.calloutEnemy);
                _config.calloutCarrier = EditorGUILayout.Toggle("  vocab: carrier", _config.calloutCarrier);
                _config.calloutEgg = EditorGUILayout.Toggle("  vocab: egg", _config.calloutEgg);
                _config.radioMemorySeconds = EditorGUILayout.FloatField("  memory (s)", _config.radioMemorySeconds);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Arena", EditorStyles.boldLabel);
            _layout.proceduralVariation = EditorGUILayout.Toggle("Procedural variation", _layout.proceduralVariation);
            using (new EditorGUI.DisabledScope(!_layout.proceduralVariation))
                _layout.variationAmount = EditorGUILayout.Slider("  amount", _layout.variationAmount, 0f, 1f);
            _layout.nestToBaseDistance = EditorGUILayout.FloatField("Nest→base distance", _layout.nestToBaseDistance);

            EditorGUILayout.Space();
            if (GUILayout.Button($"Run {_matches} matches", GUILayout.Height(30)))
                RunBatch();

            if (GUILayout.Button("Run evolution (weight search)", GUILayout.Height(24)))
                RunEvolution();

            EditorGUILayout.Space();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.TextArea(_summary, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(_lastCsvPath))
            {
                EditorGUILayout.LabelField("CSV:", _lastCsvPath, EditorStyles.miniLabel);
                if (GUILayout.Button("Reveal CSV")) EditorUtility.RevealInFinder(_lastCsvPath);
            }
        }

        private void RunBatch()
        {
            var stopwatch = Stopwatch.StartNew();
            bool cancelled = false;

            var results = BatchRunner.Run(_config, _layout, _matches, _firstSeed, (i, _) =>
            {
                if (cancelled) return;
                if (EditorUtility.DisplayCancelableProgressBar("DinoHunt batch", $"match {i + 1} / {_matches}", (i + 1) / (float)_matches))
                    cancelled = true;
            });

            EditorUtility.ClearProgressBar();
            stopwatch.Stop();

            _summary = BatchRunner.Summarize(results)
                     + $"\nwall clock      {stopwatch.ElapsedMilliseconds / 1000f:F1}s "
                     + $"({results.Count / Mathf.Max(0.001f, stopwatch.ElapsedMilliseconds / 1000f):F1} matches/sec)\n\n"
                     + MatchHealth.Report(results, _config.teamSize * 2);

            // Keep the previous batch so the next one can be compared against it — a single run
            // cannot tell you whether a change was an improvement, only a delta can.
            if (_baseline != null)
                _summary += "\n" + MatchHealth.Compare(_baseline, results, _config.teamSize * 2);
            _baseline = results;

            if (_writeCsv && results.Count > 0)
            {
                string dir = Path.Combine(Application.persistentDataPath, "DinoHunt", "batch");
                Directory.CreateDirectory(dir);
                _lastCsvPath = Path.Combine(dir, $"batch_{System.DateTime.Now:yyyyMMdd_HHmmss}_{results.Count}.csv");
                File.WriteAllText(_lastCsvPath, BatchRunner.ToCsv(results));
            }

            UnityEngine.Debug.Log("[DinoHunt] Batch complete.\n" + _summary);
        }

        private void RunEvolution()
        {
            var settings = new Evolution.Settings();
            var log = new System.Text.StringBuilder();
            bool cancelled = false;

            var population = Evolution.Run(_config, _layout, settings, (gen, best, line) =>
            {
                log.AppendLine(line);
                if (!cancelled && EditorUtility.DisplayCancelableProgressBar(
                        "DinoHunt evolution", $"generation {gen + 1} / {settings.Generations}",
                        (gen + 1) / (float)settings.Generations))
                    cancelled = true;
            });

            EditorUtility.ClearProgressBar();

            var winner = population[0].Genes;
            log.AppendLine();
            log.AppendLine($"best vector: aggression {winner.aggression:F2}  greed {winner.greed:F2}  " +
                           $"caution {winner.caution:F2}  teamplay {winner.teamplay:F2}  patience {winner.patience:F2}");
            _summary = log.ToString();

            string dir = Path.Combine(Application.persistentDataPath, "DinoHunt", "batch");
            Directory.CreateDirectory(dir);
            _lastCsvPath = Path.Combine(dir, $"evolution_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllText(_lastCsvPath, Evolution.ToCsv(population));

            UnityEngine.Debug.Log("[DinoHunt] Evolution complete.\n" + _summary);
        }
    }
}
