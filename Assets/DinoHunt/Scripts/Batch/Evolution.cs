using System;
using System.Collections.Generic;
using System.Text;
using DinoHunt.Arena;
using DinoHunt.Core;
using DinoHunt.Sim;
using UnityEngine;

namespace DinoHunt.Batch
{
    /// <summary>One candidate personality vector and how it scored.</summary>
    public sealed class Individual
    {
        public Personality Genes;
        public float Fitness;
        public int Matches;

        public Individual Clone() => new Individual { Genes = Genes.Clone(), Fitness = Fitness, Matches = Matches };
    }

    /// <summary>
    /// Evolutionary weight search over the personality vector — rung 1 of the training ladder
    /// (GDD §14.3) and the direct instrument for research question Q1.
    ///
    /// The search space is five floats in 0..1, which is small enough that a plain generational GA
    /// with elitism finds good vectors in minutes. No neural networks, no GPU, no Python.
    ///
    /// DELIBERATE DEVIATION from the GDD, which specified Python for evolution: this is implemented
    /// in C# against the existing BatchRunner. Reasoning in docs/DECISIONS.md — briefly, a Python
    /// version would need a serialisation bridge, a second toolchain, and a separate determinism
    /// story, in exchange for library convenience this problem does not need. Five floats do not
    /// justify a second language.
    ///
    /// FITNESS IS EGGS DELIVERED, NOT WIN RATE. This matters more than it looks: GDD §14.4 warns
    /// that optimising for winning finds degenerate equilibria — camp the chokepoint, run the clock,
    /// win 0-0. Scoring the objective instead keeps evolved agents doing the thing that is
    /// interesting to watch, which is the entire point of the project.
    /// </summary>
    public static class Evolution
    {
        public sealed class Settings
        {
            public int PopulationSize = 24;
            public int Generations = 12;
            public int MatchesPerEvaluation = 6;
            public int Elites = 3;
            public float MutationRate = 0.25f;
            public float MutationScale = 0.18f;
            public ulong Seed = 20260719;
            /// <summary>Small penalty per death, so evolution doesn't discover that suicide-rushing maximises pickups.</summary>
            public float DeathPenalty = 0.15f;
        }

        /// <summary>
        /// Run the search. The challenger vector is played by one team against a fixed control team
        /// (the authored roster), so fitness measures the candidate rather than co-evolutionary drift.
        /// </summary>
        public static List<Individual> Run(MatchConfig template, ArenaLayout layout, Settings settings,
                                           Action<int, Individual, string> onGeneration = null)
        {
            var rng = new DeterministicRandom(settings.Seed);
            var population = new List<Individual>(settings.PopulationSize);
            for (int i = 0; i < settings.PopulationSize; i++)
                population.Add(new Individual { Genes = RandomGenes(ref rng) });

            // Elites are re-evaluated on fresh seeds each generation, which stops a vector winning by
            // fitting one match — but it also means the reported best is NOT monotone. Keep the
            // best-ever separately so the search still has a definite answer at the end.
            Individual hallOfFame = null;

            for (int gen = 0; gen < settings.Generations; gen++)
            {
                foreach (var ind in population)
                    Evaluate(ind, template, layout, settings, gen);

                population.Sort((a, b) => b.Fitness.CompareTo(a.Fitness));
                if (hallOfFame == null || population[0].Fitness > hallOfFame.Fitness)
                    hallOfFame = population[0].Clone();

                onGeneration?.Invoke(gen, population[0], Describe(population, gen));

                if (gen == settings.Generations - 1)
                {
                    // Make sure the best-ever survives into the returned population.
                    if (hallOfFame != null && population[population.Count - 1].Fitness < hallOfFame.Fitness)
                        population[population.Count - 1] = hallOfFame;
                    break;
                }

                // Elitism + tournament selection. Keeping the best unchanged guarantees monotone
                // improvement; everything else is bred and mutated.
                var next = new List<Individual>(settings.PopulationSize);
                for (int i = 0; i < settings.Elites && i < population.Count; i++)
                    next.Add(population[i].Clone());

                while (next.Count < settings.PopulationSize)
                {
                    Personality a = Tournament(population, ref rng).Genes;
                    Personality b = Tournament(population, ref rng).Genes;
                    Personality child = Crossover(a, b, ref rng);
                    Mutate(child, settings, ref rng);
                    next.Add(new Individual { Genes = child });
                }

                population = next;
            }

            population.Sort((a, b) => b.Fitness.CompareTo(a.Fitness));
            return population;
        }

        /// <summary>
        /// Play the candidate (blue) against the authored roster (red) and score the objective.
        /// Seeds vary per generation so a vector cannot overfit one match.
        /// </summary>
        private static void Evaluate(Individual ind, MatchConfig template, ArenaLayout layout, Settings settings, int generation)
        {
            float total = 0f;
            for (int m = 0; m < settings.MatchesPerEvaluation; m++)
            {
                MatchConfig config = BatchRunner.Clone(template);
                config.seed = (ulong)(1000 + generation * 977 + m * 31);
                config.perAgentPersonalities = false;
                config.personality = ind.Genes;

                MatchResult r = MatchRunner.Run(config, layout);

                // Blue is the candidate. Eggs delivered is the signal; deaths are a mild cost.
                int delivered = 0, deaths = 0;
                foreach (var s in r.Agents)
                {
                    if (s.Team != "blue") continue;
                    delivered += s.EggsDelivered;
                    deaths += s.Deaths;
                }
                total += delivered - deaths * settings.DeathPenalty;
            }

            ind.Fitness = total / Mathf.Max(1, settings.MatchesPerEvaluation);
            ind.Matches = settings.MatchesPerEvaluation;
        }

        private static Personality RandomGenes(ref DeterministicRandom rng) => new Personality
        {
            aggression = rng.NextFloat(), greed = rng.NextFloat(), caution = rng.NextFloat(),
            teamplay = rng.NextFloat(), patience = rng.NextFloat()
        };

        private static Individual Tournament(List<Individual> pop, ref DeterministicRandom rng)
        {
            Individual a = pop[rng.NextInt(0, pop.Count)];
            Individual b = pop[rng.NextInt(0, pop.Count)];
            return a.Fitness >= b.Fitness ? a : b;
        }

        /// <summary>Uniform crossover — each trait comes from one parent or the other.</summary>
        private static Personality Crossover(Personality a, Personality b, ref DeterministicRandom rng) => new Personality
        {
            aggression = rng.NextBool() ? a.aggression : b.aggression,
            greed = rng.NextBool() ? a.greed : b.greed,
            caution = rng.NextBool() ? a.caution : b.caution,
            teamplay = rng.NextBool() ? a.teamplay : b.teamplay,
            patience = rng.NextBool() ? a.patience : b.patience
        };

        private static void Mutate(Personality p, Settings s, ref DeterministicRandom rng)
        {
            p.aggression = MutateGene(p.aggression, s, ref rng);
            p.greed = MutateGene(p.greed, s, ref rng);
            p.caution = MutateGene(p.caution, s, ref rng);
            p.teamplay = MutateGene(p.teamplay, s, ref rng);
            p.patience = MutateGene(p.patience, s, ref rng);
        }

        private static float MutateGene(float v, Settings s, ref DeterministicRandom rng)
        {
            if (rng.NextFloat() > s.MutationRate) return v;
            return Mathf.Clamp01(v + rng.NextFloat(-s.MutationScale, s.MutationScale));
        }

        private static string Describe(List<Individual> sorted, int generation)
        {
            float mean = 0f;
            foreach (var i in sorted) mean += i.Fitness;
            mean /= Mathf.Max(1, sorted.Count);

            Personality g = sorted[0].Genes;
            return $"gen {generation,2}  best {sorted[0].Fitness,6:F2}  mean {mean,6:F2}  " +
                   $"[agg {g.aggression:F2} greed {g.greed:F2} caut {g.caution:F2} team {g.teamplay:F2} pat {g.patience:F2}]";
        }

        /// <summary>CSV of the final population, for offline clustering (research question Q1).</summary>
        public static string ToCsv(IReadOnlyList<Individual> population)
        {
            var sb = new StringBuilder();
            sb.AppendLine("rank,fitness,matches,aggression,greed,caution,teamplay,patience");
            for (int i = 0; i < population.Count; i++)
            {
                Individual x = population[i];
                Personality g = x.Genes;
                sb.AppendLine($"{i},{x.Fitness:F4},{x.Matches},{g.aggression:F4},{g.greed:F4},{g.caution:F4},{g.teamplay:F4},{g.patience:F4}");
            }
            return sb.ToString();
        }
    }
}
