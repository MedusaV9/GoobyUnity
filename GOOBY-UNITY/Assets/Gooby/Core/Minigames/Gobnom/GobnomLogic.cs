using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Gobnom
{
    /// <summary>
    /// GOB NOM (Doc G §5, "Cut the Rope") — port of gobnom_logic.gd: a pure, deterministic 60-Hz simulation. The candy
    /// is a Verlet point mass, ropes are ONE-sided distance constraints (they pull, never push), randomness only comes
    /// from <see cref="GoobyRng"/> (fan turbulence). The view renders a <see cref="GobnomRun"/> and calls the action
    /// functions. World 960×540 (landscape; portrait scales the view). Coop: split axis + owner per anchor — cut/tap
    /// actions carry a player ("a"/"b") and the sim refuses foreign anchors / the other half; solo runs pass "solo".
    /// Vectors are float32 like Godot's Vector2 (<see cref="V2"/>); scalars from the data stay double like GDScript.
    /// </summary>
    public static class GobnomLogic
    {
        public const int Tps = 60;
        public const double OutMargin = 80.0;
        public const string PlayerSolo = "solo", PlayerA = "a", PlayerB = "b";

        public static GobnomRun NewRun(JObject level, JObject balance, long seed = 1)
        {
            var candy = level["candy"] as JObject ?? new JObject { ["x"] = 480.0, ["y"] = 140.0 };
            var mouth = level["mouth"] as JObject ?? new JObject { ["x"] = 480.0, ["y"] = 470.0 };
            var pos = new V2(Num(candy, "x", 480), Num(candy, "y", 140));
            var coop = Str(level, "kind", "campaign") == "coop";
            var physics = balance?["physics"] as JObject ?? new JObject();
            var world = balance?["world"] as JObject ?? new JObject();
            var run = new GobnomRun
            {
                Rng = new GoobyRng(seed),
                Seed = seed,
                Level = level,
                Physics = new GobnomPhysics(physics),
                WorldW = Num(world, "w", 960.0),
                WorldH = Num(world, "h", 540.0),
                Coop = coop,
                CandyPos = pos,
                CandyPrev = pos,
                Mouth = new V2(Num(mouth, "x", 480), Num(mouth, "y", 470)),
                MaxCuts = Int(level, "max_cuts", -1),
            };
            if (coop && level["split"] is JObject split && split.HasValues)
            {
                run.HasSplit = true;
                run.SplitAxis = Str(split, "axis", "x");
                run.SplitAt = split["at"] != null ? Num(split, "at", 0) : double.NaN;
            }
            InitRopes(run, level);
            foreach (var (row, i) in Rows(level, "bubbles"))
                run.Bubbles.Add(new GobnomBubble { Id = i, Pos = new V2(Num(row, "x", 0), Num(row, "y", 0)), R = Num(row, "r", 26.0) });
            foreach (var (row, i) in Rows(level, "cushions"))
                run.Cushions.Add(new GobnomCushion
                {
                    Id = i,
                    Pos = new V2(Num(row, "x", 0), Num(row, "y", 0)),
                    Dir = new V2(Num(row, "dx", 1), Num(row, "dy", 0)).Normalized(),
                    Power = Num(row, "power", 420.0),
                    Range = Num(row, "range", 260.0),
                    HalfW = Num(row, "half_w", 60.0),
                    Charges = Int(row, "charges", -1),
                    Owner = Str(row, "owner", "any"),
                });
            foreach (var (row, i) in Rows(level, "fans"))
                run.Fans.Add(new GobnomFan
                {
                    Id = i,
                    Pos = new V2(Num(row, "x", 0), Num(row, "y", 0)),
                    Dir = new V2(Num(row, "dx", 1), Num(row, "dy", 0)).Normalized(),
                    Force = Num(row, "force", 260.0),
                    Range = Num(row, "range", 0.0),
                    HalfW = Num(row, "half_w", 80.0),
                    On = Bool(row, "on", true),
                    Toggleable = Bool(row, "toggleable", false),
                    Turbulence = Num(row, "turbulence", 0.0),
                    Owner = Str(row, "owner", "any"),
                });
            foreach (var (row, i) in Rows(level, "shooters"))
                run.Shooters.Add(new GobnomShooter
                {
                    Id = i,
                    Pos = new V2(Num(row, "x", 0), Num(row, "y", 0)),
                    R = Num(row, "r", 90.0),
                    RestFactor = Num(row, "rest_factor", 1.0),
                    RopeId = 100 + i, // shooter ropes get ids from 100 so the level's ropes 0..n stay stable
                    Owner = Str(row, "owner", "any"),
                });
            foreach (var (row, _) in Rows(level, "spikes")) run.Spikes.Add(new GobnomBox(Num(row, "x", 0), Num(row, "y", 0), Num(row, "w", 0), Num(row, "h", 0)));
            foreach (var (row, _) in Rows(level, "clouds")) run.Clouds.Add(new GobnomBox(Num(row, "x", 0), Num(row, "y", 0), Num(row, "w", 0), Num(row, "h", 0)));
            foreach (var (row, i) in Rows(level, "jars")) run.Jars.Add(new GobnomJar { Id = i, Pos = new V2(Num(row, "x", 0), Num(row, "y", 0)) });
            if (coop) run.LastSide = SideOf(run, pos);
            return run;
        }

        /// <summary>One 60-Hz step. Mutates the run and returns the tick's events.</summary>
        public static List<GobnomEvent> Step(GobnomRun run)
        {
            run.Events = new List<GobnomEvent>();
            if (run.Outcome != "") return run.Events;
            run.Tick += 1;
            Integrate(run);
            ApplyRopeConstraints(run);
            ShooterCheck(run);
            BubbleCheck(run);
            JarCheck(run);
            HazardCheck(run);
            GoalCheck(run);
            SideCheck(run);
            return run.Events;
        }

        // ── actions (view, solver and lockstep call ONLY these) ──

        /// <summary>Cut a rope by id. Reasons: outcome | unknown | already_cut | cut_limit | wrong_side.</summary>
        public static GobnomResult CutRope(GobnomRun run, int ropeId, string player = PlayerSolo)
        {
            if (run.Outcome != "") return GobnomResult.No("outcome");
            var rope = RopeById(run, ropeId);
            if (rope == null) return GobnomResult.No("unknown");
            if (rope.Cut) return GobnomResult.No("already_cut");
            if (!CutBudgetLeft(run))
            {
                Push(run, new GobnomEvent { Kind = "denied", Reason = "cut_limit", Player = player });
                return GobnomResult.No("cut_limit");
            }
            if (!Owns(run, player, rope.Owner, rope.Anchor))
            {
                Push(run, new GobnomEvent { Kind = "denied", Reason = "wrong_side", Player = player });
                return GobnomResult.No("wrong_side");
            }
            rope.Cut = true;
            run.CutsUsed += 1;
            Push(run, new GobnomEvent { Kind = "cut", Rope = ropeId, Player = player, At = (rope.Anchor + run.CandyPos) * 0.5f });
            return GobnomResult.Yes();
        }

        /// <summary>Swipe cut: every uncut rope whose anchor→candy segment crosses the swipe (several per swipe).</summary>
        public static List<int> CutSegment(GobnomRun run, V2 from, V2 to, string player = PlayerSolo)
        {
            var cutIds = new List<int>();
            if (run.Outcome != "" || from.IsEqualApprox(to)) return cutIds;
            foreach (var rope in run.Ropes.ToArray())
            {
                if (rope.Cut) continue;
                if (!GodotMath.SegmentsIntersect(from, to, rope.Anchor, run.CandyPos)) continue;
                if (CutRope(run, rope.Id, player).Ok) cutIds.Add(rope.Id);
            }
            return cutIds;
        }

        /// <summary>Tap a bubble = pop (a held candy falls free again).</summary>
        public static GobnomResult PopBubble(GobnomRun run, int bubbleId, string player = PlayerSolo)
        {
            if (run.Outcome != "") return GobnomResult.No("outcome");
            foreach (var bubble in run.Bubbles)
            {
                if (bubble.Id != bubbleId || bubble.Popped) continue;
                var at = bubble.Holds ? run.CandyPos : bubble.Pos;
                if (!Owns(run, player, "any", at))
                {
                    Push(run, new GobnomEvent { Kind = "denied", Reason = "wrong_side", Player = player });
                    return GobnomResult.No("wrong_side");
                }
                bubble.Popped = true;
                if (bubble.Holds)
                {
                    bubble.Holds = false;
                    run.InBubble = false;
                    run.BubbleId = -1;
                }
                Push(run, new GobnomEvent { Kind = "pop", Bubble = bubbleId, At = at, Player = player });
                return GobnomResult.Yes();
            }
            return GobnomResult.No("unknown");
        }

        /// <summary>Fire an air cushion: an impulse when the candy is in its beam. Charges/cooldown only spent on ok.</summary>
        public static GobnomResult PuffCushion(GobnomRun run, int cushionId, string player = PlayerSolo)
        {
            if (run.Outcome != "") return GobnomResult.No("outcome");
            foreach (var cushion in run.Cushions)
            {
                if (cushion.Id != cushionId) continue;
                if (!Owns(run, player, cushion.Owner, cushion.Pos))
                {
                    Push(run, new GobnomEvent { Kind = "denied", Reason = "wrong_side", Player = player });
                    return GobnomResult.No("wrong_side");
                }
                if (cushion.Charges == 0) return GobnomResult.No("empty");
                if (run.Tick < cushion.ReadyTick) return GobnomResult.No("cooldown");
                cushion.ReadyTick = run.Tick + run.Physics.CushionCooldownTicks;
                if (cushion.Charges > 0) cushion.Charges -= 1;
                var hit = InBeam(run.CandyPos, cushion.Pos, cushion.Dir, cushion.Range, cushion.HalfW);
                if (hit) AddVelocity(run, cushion.Dir * cushion.Power);
                Push(run, new GobnomEvent { Kind = "puff", Cushion = cushionId, Hit = hit, Player = player });
                return GobnomResult.Yes(hit);
            }
            return GobnomResult.No("unknown");
        }

        /// <summary>Switch a fan on/off (toggleable fans only).</summary>
        public static GobnomResult ToggleFan(GobnomRun run, int fanId, string player = PlayerSolo)
        {
            if (run.Outcome != "") return GobnomResult.No("outcome");
            foreach (var fan in run.Fans)
            {
                if (fan.Id != fanId) continue;
                if (!fan.Toggleable) return GobnomResult.No("fixed");
                if (!Owns(run, player, fan.Owner, fan.Pos))
                {
                    Push(run, new GobnomEvent { Kind = "denied", Reason = "wrong_side", Player = player });
                    return GobnomResult.No("wrong_side");
                }
                fan.On = !fan.On;
                Push(run, new GobnomEvent { Kind = "fan", Fan = fanId, On = fan.On, Player = player });
                return GobnomResult.Yes();
            }
            return GobnomResult.No("unknown");
        }

        /// <summary>Slide a rail anchor (t in 0..1, clamped).</summary>
        public static GobnomResult MoveAnchor(GobnomRun run, int ropeId, double t, string player = PlayerSolo)
        {
            if (run.Outcome != "") return GobnomResult.No("outcome");
            var rope = RopeById(run, ropeId);
            if (rope == null || rope.Rail == null) return GobnomResult.No("no_rail");
            if (!Owns(run, player, rope.Owner, rope.Anchor))
            {
                Push(run, new GobnomEvent { Kind = "denied", Reason = "wrong_side", Player = player });
                return GobnomResult.No("wrong_side");
            }
            var rail = rope.Rail;
            rail.T = Math.Min(Math.Max(t, 0.0), 1.0);
            rope.Anchor = rail.From.Lerp(rail.To, (float)rail.T);
            Push(run, new GobnomEvent { Kind = "slide", Rope = ropeId, T = rail.T, Player = player });
            return GobnomResult.Yes();
        }

        // ── queries ──

        public static bool IsOver(GobnomRun run) => run.Outcome != "";

        /// <summary>Candy velocity in world units per second (from the Verlet pair).</summary>
        public static V2 CandyVelocity(GobnomRun run) => (run.CandyPos - run.CandyPrev) * (float)Tps;

        /// <summary>Coop half of a point ("a" = left/top, "b" = right/bottom); without a split always "a".</summary>
        public static string SideOf(GobnomRun run, V2 at)
        {
            if (!run.HasSplit) return PlayerA;
            if (run.SplitAxis == "y") return at.Y < (double.IsNaN(run.SplitAt) ? 270.0 : run.SplitAt) ? PlayerA : PlayerB;
            return at.X < (double.IsNaN(run.SplitAt) ? 480.0 : run.SplitAt) ? PlayerA : PlayerB;
        }

        /// <summary>Cuts left (-1 = unlimited).</summary>
        public static int CutsLeft(GobnomRun run) => run.MaxCuts < 0 ? -1 : Math.Max(0, run.MaxCuts - run.CutsUsed);

        /// <summary>Stars of a win = Nutella jars collected (0–3, Doc G §5.2).</summary>
        public static int StarsFor(int jarsTaken) => Math.Min(Math.Max(jarsTaken, 0), 3);

        /// <summary>
        /// Deterministic state hash for replays and the lockstep desync guard (rng/level/balance/events excluded —
        /// their effect lives in the fields). Exact float bits, so two clients agree only when they simulate alike.
        /// </summary>
        public static string StateHash(GobnomRun run)
        {
            var sb = new StringBuilder(256);
            sb.Append(run.Tick).Append('|');
            Bits(sb, run.CandyPos); Bits(sb, run.CandyPrev);
            sb.Append(run.InBubble ? 1 : 0).Append(',').Append(run.BubbleId).Append('|');
            foreach (var r in run.Ropes) { sb.Append(r.Id).Append(r.Cut ? 'c' : 'u'); Bits(sb, r.Anchor); sb.Append(r.Rest.ToString("R", CultureInfo.InvariantCulture)).Append(';'); }
            sb.Append('|');
            foreach (var b in run.Bubbles) { sb.Append(b.Popped ? 'p' : b.Holds ? 'h' : 'f'); Bits(sb, b.Pos); }
            sb.Append('|');
            foreach (var c in run.Cushions) sb.Append(c.Charges).Append(':').Append(c.ReadyTick).Append(';');
            sb.Append('|');
            foreach (var f in run.Fans) sb.Append(f.On ? '1' : '0');
            sb.Append('|');
            foreach (var s in run.Shooters) sb.Append(s.Fired ? '1' : '0');
            sb.Append('|');
            foreach (var j in run.Jars) sb.Append(j.Taken ? '1' : '0');
            sb.Append('|').Append(run.CutsUsed).Append(',').Append(run.JarsTaken).Append(',').Append(run.SideChanges).Append(',').Append(run.Outcome);
            ulong h = 14695981039346656037UL;
            foreach (var ch in sb.ToString()) { h ^= ch; h *= 1099511628211UL; }
            return h.ToString("x16");
        }

        private static void Bits(StringBuilder sb, V2 v) =>
            sb.Append(BitConverter.SingleToInt32Bits(v.X).ToString("x8")).Append(BitConverter.SingleToInt32Bits(v.Y).ToString("x8")).Append(';');

        public static void Push(GobnomRun run, GobnomEvent ev) => run.Events.Add(ev);

        // ── physics core ──

        /// <summary>Verlet: gravity OR bubble lift, fan wind, cloud brake, global damping, anti-explosion step cap.</summary>
        private static void Integrate(GobnomRun run)
        {
            var p = run.Physics;
            const double dt = 1.0 / Tps;
            var accel = new V2(0.0, p.Gravity);
            if (run.InBubble) accel = new V2(0.0, -p.BubbleLift);
            accel += FanForce(run);
            var damping = p.Damping;
            if (run.InBubble) damping = p.BubbleDamping;
            else if (InCloud(run)) damping = p.CloudDamping;
            var vel = (run.CandyPos - run.CandyPrev) * damping;
            vel = vel.LimitLength((float)p.MaxStep);
            run.CandyPrev = run.CandyPos;
            run.CandyPos = run.CandyPos + vel + accel * dt * dt;
            if (run.InBubble) CarryBubble(run);
        }

        /// <summary>One-sided distance constraints: ropes only pull (dist &gt; rest), several passes for multi-rope rigs.</summary>
        private static void ApplyRopeConstraints(GobnomRun run)
        {
            for (var i = 0; i < run.Physics.Iterations; i++)
                foreach (var rope in run.Ropes)
                {
                    if (rope.Cut) continue;
                    var delta = run.CandyPos - rope.Anchor;
                    double dist = delta.Length;
                    if (dist > rope.Rest && dist > 0.001) run.CandyPos = rope.Anchor + delta / dist * rope.Rest;
                }
        }

        /// <summary>Wind of every active fan (beam OR global); turbulence draws on the run's GoobyRng (its only consumer).</summary>
        private static V2 FanForce(GobnomRun run)
        {
            var force = V2.Zero;
            var pos = run.CandyPos;
            foreach (var fan in run.Fans)
            {
                if (!fan.On) continue;
                if (fan.Range > 0.0 && !InBeam(pos, fan.Pos, fan.Dir, fan.Range, fan.HalfW)) continue;
                var strength = fan.Force;
                if (fan.Turbulence > 0.0) strength *= 1.0 + fan.Turbulence * (run.Rng.Next() * 2.0 - 1.0);
                force += fan.Dir * strength;
            }
            return force;
        }

        /// <summary>Auto-rope shooter: fires ONE new rope once the candy is in range (rest = distance × factor).</summary>
        private static void ShooterCheck(GobnomRun run)
        {
            var pos = run.CandyPos;
            foreach (var shooter in run.Shooters)
            {
                if (shooter.Fired) continue;
                double dist = pos.DistanceTo(shooter.Pos);
                if (dist > shooter.R) continue;
                shooter.Fired = true;
                var rope = new GobnomRope { Id = shooter.RopeId, Anchor = shooter.Pos, Rest = Math.Max(24.0, dist * shooter.RestFactor), Owner = shooter.Owner };
                run.Ropes.Add(rope);
                Push(run, new GobnomEvent { Kind = "shoot", Shooter = shooter.Id, Rope = rope.Id });
            }
        }

        /// <summary>Bubbles catch the candy on contact (once per bubble).</summary>
        private static void BubbleCheck(GobnomRun run)
        {
            if (run.InBubble) return;
            var pos = run.CandyPos;
            var candyR = run.Physics.CandyR;
            foreach (var bubble in run.Bubbles)
            {
                if (bubble.Popped || bubble.Holds) continue;
                if (pos.DistanceTo(bubble.Pos) > bubble.R + candyR) continue;
                bubble.Holds = true;
                run.InBubble = true;
                run.BubbleId = bubble.Id;
                Push(run, new GobnomEvent { Kind = "catch", Bubble = bubble.Id });
                return;
            }
        }

        private static void JarCheck(GobnomRun run)
        {
            var pos = run.CandyPos;
            var candyR = run.Physics.CandyR;
            var jarR = run.Physics.JarR;
            foreach (var jar in run.Jars)
            {
                if (jar.Taken) continue;
                if (pos.DistanceTo(jar.Pos) > jarR + candyR) continue;
                jar.Taken = true;
                run.JarsTaken += 1;
                Push(run, new GobnomEvent { Kind = "jar", Jar = jar.Id, At = jar.Pos });
            }
        }

        /// <summary>Spike boards and the world edges (bottom AND top — a bubble that floats away fails too).</summary>
        private static void HazardCheck(GobnomRun run)
        {
            if (run.Outcome != "") return;
            var pos = run.CandyPos;
            var candyR = run.Physics.CandyR;
            foreach (var spike in run.Spikes)
            {
                var min = new V2(spike.X, spike.Y);
                var max = min + new V2(spike.W, spike.H);
                var closest = pos.Clamp(min, max);
                if (pos.DistanceTo(closest) <= candyR)
                {
                    run.Outcome = "lost";
                    Push(run, new GobnomEvent { Kind = "spike", At = pos });
                    return;
                }
            }
            var outX = pos.X < -OutMargin || pos.X > run.WorldW + OutMargin;
            var outY = pos.Y < -OutMargin || pos.Y > run.WorldH + OutMargin;
            if (outX || outY)
            {
                run.Outcome = "lost";
                Push(run, new GobnomEvent { Kind = "fell", At = pos });
            }
        }

        /// <summary>Gooby's mouth: candy inside → NOM, level cleared.</summary>
        private static void GoalCheck(GobnomRun run)
        {
            if (run.Outcome != "") return;
            if (run.CandyPos.DistanceTo(run.Mouth) <= run.Physics.MouthR)
            {
                run.Outcome = "won";
                Push(run, new GobnomEvent { Kind = "nom", Jars = run.JarsTaken });
            }
        }

        /// <summary>Side changes of coop levels (the CN rule: the candy MUST cross over).</summary>
        private static void SideCheck(GobnomRun run)
        {
            if (!run.Coop) return;
            var side = SideOf(run, run.CandyPos);
            if (side == run.LastSide) return;
            run.LastSide = side;
            run.SideChanges += 1;
            Push(run, new GobnomEvent { Kind = "side", Side = side });
        }

        // ── helpers ──

        private static void AddVelocity(GobnomRun run, V2 deltaV) => run.CandyPrev = run.CandyPrev - deltaV / (float)Tps;

        private static void CarryBubble(GobnomRun run)
        {
            foreach (var bubble in run.Bubbles)
                if (bubble.Id == run.BubbleId) { bubble.Pos = run.CandyPos; return; }
        }

        private static bool InCloud(GobnomRun run)
        {
            foreach (var c in run.Clouds)
                if (GodotMath.RectHasPoint((float)c.X, (float)c.Y, (float)c.W, (float)c.H, run.CandyPos)) return true;
            return false;
        }

        /// <summary>Point inside a directed air beam (rectangle from origin along dir)?</summary>
        public static bool InBeam(V2 point, V2 origin, V2 dir, double range, double halfW)
        {
            var delta = point - origin;
            double along = delta.Dot(dir);
            double across = Math.Abs(delta.Cross(dir));
            return along >= -8.0 && along <= range && across <= halfW;
        }

        /// <summary>Action gate: solo may do anything; coop checks the owner tag, else the half of the interaction point.</summary>
        private static bool Owns(GobnomRun run, string player, string ownerTag, V2 at)
        {
            if (!run.Coop || player == PlayerSolo) return true;
            if (ownerTag == PlayerA || ownerTag == PlayerB) return ownerTag == player;
            return SideOf(run, at) == player;
        }

        private static bool CutBudgetLeft(GobnomRun run) => run.MaxCuts < 0 || run.CutsUsed < run.MaxCuts;

        public static GobnomRope RopeById(GobnomRun run, int ropeId)
        {
            foreach (var rope in run.Ropes) if (rope.Id == ropeId) return rope;
            return null;
        }

        private static void InitRopes(GobnomRun run, JObject level)
        {
            foreach (var (row, i) in Rows(level, "ropes"))
            {
                var anchor = new V2(Num(row, "x", 480), Num(row, "y", 100));
                GobnomRail rail = null;
                if (row["rail"] is JObject def)
                {
                    var t = Num(def, "t", 0.0);
                    var from = new V2(def["x1"] != null ? Num(def, "x1", 0) : anchor.X, def["y1"] != null ? Num(def, "y1", 0) : anchor.Y);
                    var to = new V2(def["x2"] != null ? Num(def, "x2", 0) : anchor.X, def["y2"] != null ? Num(def, "y2", 0) : anchor.Y);
                    rail = new GobnomRail { From = from, To = to, T = t };
                    anchor = from.Lerp(to, (float)t);
                }
                run.Ropes.Add(new GobnomRope { Id = i, Anchor = anchor, Rest = Num(row, "rest", 100.0), Owner = Str(row, "owner", "any"), Rail = rail });
            }
        }

        private static IEnumerable<(JObject row, int index)> Rows(JObject level, string key)
        {
            if (!(level[key] is JArray arr)) yield break;
            for (var i = 0; i < arr.Count; i++) yield return (arr[i] as JObject ?? new JObject(), i);
        }

        internal static double Num(JObject o, string key, double fallback)
        {
            var t = o?[key];
            if (t == null) return fallback;
            switch (t.Type)
            {
                case JTokenType.Integer:
                case JTokenType.Float:
                    return t.Value<double>();
                case JTokenType.Boolean:
                    return t.Value<bool>() ? 1.0 : 0.0;
                case JTokenType.String:
                    return double.TryParse(t.Value<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0.0;
                default:
                    return 0.0;
            }
        }

        internal static int Int(JObject o, string key, int fallback) => o?[key] == null ? fallback : (int)Num(o, key, fallback);

        internal static string Str(JObject o, string key, string fallback)
        {
            var t = o?[key];
            return t == null || t.Type == JTokenType.Null ? fallback : t.Type == JTokenType.String ? t.Value<string>() : t.ToString();
        }

        internal static bool Bool(JObject o, string key, bool fallback)
        {
            var t = o?[key];
            if (t == null || t.Type == JTokenType.Null) return fallback;
            if (t.Type == JTokenType.Boolean) return t.Value<bool>();
            return Num(o, key, 0) != 0.0;
        }
    }

    /// <summary>Physics numbers of the balance file (defaults as in gobnom_logic.gd).</summary>
    public sealed class GobnomPhysics
    {
        public readonly double Gravity, BubbleLift, Damping, BubbleDamping, CloudDamping, MaxStep, CandyR, JarR, MouthR;
        public readonly int Iterations, CushionCooldownTicks;

        public GobnomPhysics(JObject p)
        {
            Gravity = GobnomLogic.Num(p, "gravity", 900.0);
            BubbleLift = GobnomLogic.Num(p, "bubble_lift", 420.0);
            Damping = GobnomLogic.Num(p, "damping", 0.998);
            BubbleDamping = GobnomLogic.Num(p, "bubble_damping", 0.96);
            CloudDamping = GobnomLogic.Num(p, "cloud_damping", 0.82);
            MaxStep = GobnomLogic.Num(p, "max_step", 24.0);
            CandyR = GobnomLogic.Num(p, "candy_r", 14.0);
            JarR = GobnomLogic.Num(p, "jar_r", 24.0);
            MouthR = GobnomLogic.Num(p, "mouth_r", 36.0);
            Iterations = GobnomLogic.Int(p, "iterations", 3);
            CushionCooldownTicks = GobnomLogic.Int(p, "cushion_cooldown_ticks", 12);
        }
    }

    public sealed class GobnomRun
    {
        public int Tick;
        public GoobyRng Rng;
        public long Seed;
        public JObject Level;
        public GobnomPhysics Physics;
        public double WorldW = 960.0, WorldH = 540.0;
        public bool Coop, HasSplit;
        public string SplitAxis = "x";
        public double SplitAt = double.NaN;
        public V2 CandyPos, CandyPrev, Mouth;
        public bool InBubble;
        public int BubbleId = -1;
        public readonly List<GobnomRope> Ropes = new List<GobnomRope>();
        public readonly List<GobnomBubble> Bubbles = new List<GobnomBubble>();
        public readonly List<GobnomCushion> Cushions = new List<GobnomCushion>();
        public readonly List<GobnomFan> Fans = new List<GobnomFan>();
        public readonly List<GobnomShooter> Shooters = new List<GobnomShooter>();
        public readonly List<GobnomBox> Spikes = new List<GobnomBox>();
        public readonly List<GobnomBox> Clouds = new List<GobnomBox>();
        public readonly List<GobnomJar> Jars = new List<GobnomJar>();
        public int CutsUsed, MaxCuts = -1, JarsTaken, SideChanges;
        public string LastSide = "", Outcome = "";
        public List<GobnomEvent> Events = new List<GobnomEvent>();
    }

    public sealed class GobnomRope
    {
        public int Id;
        public V2 Anchor;
        public double Rest;
        public bool Cut;
        public string Owner = "any";
        public GobnomRail Rail;
    }

    public sealed class GobnomRail
    {
        public V2 From, To;
        public double T;
    }

    public sealed class GobnomBubble
    {
        public int Id;
        public V2 Pos;
        public double R;
        public bool Popped, Holds;
    }

    public sealed class GobnomCushion
    {
        public int Id, Charges = -1, ReadyTick;
        public V2 Pos, Dir;
        public double Power, Range, HalfW;
        public string Owner = "any";
    }

    public sealed class GobnomFan
    {
        public int Id;
        public V2 Pos, Dir;
        public double Force, Range, HalfW, Turbulence;
        public bool On, Toggleable;
        public string Owner = "any";
    }

    public sealed class GobnomShooter
    {
        public int Id, RopeId;
        public V2 Pos;
        public double R, RestFactor;
        public bool Fired;
        public string Owner = "any";
    }

    public sealed class GobnomJar
    {
        public int Id;
        public V2 Pos;
        public bool Taken;
    }

    public readonly struct GobnomBox
    {
        public readonly double X, Y, W, H;
        public GobnomBox(double x, double y, double w, double h) { X = x; Y = y; W = w; H = h; }
    }

    /// <summary>A tick event (kind: cut/denied/jar/nom/fell/spike/pop/catch/puff/shoot/fan/slide/side).</summary>
    public sealed class GobnomEvent
    {
        public string Kind, Player, Reason, Side;
        public int Rope = -1, Bubble = -1, Cushion = -1, Fan = -1, Shooter = -1, Jar = -1, Jars;
        public V2 At;
        public bool Hit, On;
        public double T;
    }

    public readonly struct GobnomResult
    {
        public readonly bool Ok, Hit;
        public readonly string Reason;
        private GobnomResult(bool ok, string reason, bool hit) { Ok = ok; Reason = reason; Hit = hit; }
        public static GobnomResult Yes(bool hit = false) => new GobnomResult(true, "", hit);
        public static GobnomResult No(string reason) => new GobnomResult(false, reason, false);
    }
}
