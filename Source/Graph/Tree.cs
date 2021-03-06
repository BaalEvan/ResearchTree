// Tree.cs
// Copyright Karel Kroeze, 2020-2020

//using Multiplayer.API;

using static FluffyResearchTree.Constants;

namespace FluffyResearchTree
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.Serialization;
    using System.Runtime.Serialization.Formatters.Binary;
    using System.Security.Cryptography;
    using System.Text;
    using global::ResearchTree;
    using global::ResearchTree.Graph;
    using RimWorld;
    using UnityEngine;
    using Verse;

    [Serializable]
    public class Tree
    {
     public const string ALL_TAB_NAME = "All";

        public static Tree ActiveTree;
        public static Tree AllTreeTab;
        public static Dictionary<string, Tree> Trees = new Dictionary<string, Tree>();
        public static List<ResearchTabDef> Tabs = new List<ResearchTabDef>();
        private List<Edge<Node, Node>> _edges;

        private bool _initializing;
        private List<Node> _nodes;
        private List<TechLevel> _relevantTechLevels;
        private Dictionary<TechLevel, IntRange> _techLevelBounds;

        public bool Initialized;

        public bool OrderDirty;
        public IntVec2 Size = IntVec2.Zero;
        public string TabName;
        public string TabLabel;
        private Rect _treeRect;

        public Rect TreeRect
        {
            get
            {
                if (_treeRect == default)
                {
                    var width = Size.x * (NodeSize.x + NodeMargins.x);
                    var height =Size.z * (NodeSize.y + NodeMargins.y);
                    _treeRect = new Rect(0f, 0f, width, height);
                }

                return _treeRect;
            }
        }


        public Tree(ResearchTabDef tab)
        {
            TabName = tab.defName;
            TabLabel = (!tab.LabelCap.NullOrEmpty()?tab.LabelCap.Resolve():tab.defName);
            Trees.Add(TabName, this);
            Tabs.Add(tab);
            // Initialize();
        }

        public Tree(){}
        public static Tree AllTab()
        {
            var allTree = new Tree()
            {
                TabName = ALL_TAB_NAME,
                TabLabel = ALL_TAB_NAME,

            };
            AllTreeTab = allTree;

            Trees.Add(ALL_TAB_NAME, allTree);
            Tabs.Add(new ResearchTabDef(){defName = ALL_TAB_NAME});
            return allTree;
        }

        public Dictionary<TechLevel, IntRange> TechLevelBounds
        {
            get
            {
                if (_techLevelBounds == null)
                    throw new Exception("TechLevelBounds called before they are set.");
                return _techLevelBounds;
            }
        }

        public List<TechLevel> RelevantTechLevels
        {
            get
            {
                if (_relevantTechLevels == null)
                    _relevantTechLevels = Enum.GetValues(typeof(TechLevel))
                        .Cast<TechLevel>()
                        // filter down to relevant tech levels only.
                        .Where(
                            tl => (TabName==ALL_TAB_NAME?DefDatabase<ResearchProjectDef>.AllDefsListForReading:DefDatabase<ResearchProjectDef>.AllDefsListForReading.Where(def => def.tab.defName == TabName))
                                .Any(rp => rp.techLevel == tl))
                        .ToList();
                return _relevantTechLevels;
            }
        }

        public List<Node> Nodes
        {
            get
            {
                if (_nodes == null)
                    PopulateNodes();

                return _nodes;
            }
        }

        public List<Edge<Node, Node>> Edges
        {
            get
            {
                if (_edges == null)
                    throw new Exception("Trying to access edges before they are initialized.");

                return _edges;
            }
        }

      public void DrawTab(Rect visibleRect)
        {
            var mouseOver = Mouse.IsOver(visibleRect);
            var isActive = ActiveTree.TabName == TabName;
            if (Event.current.type == EventType.Repaint)
            {
                // researches that are completed or could be started immediately, and that have the required building(s) available
                GUI.color = mouseOver ? GenUI.MouseoverColor : Assets.ColorCompleted[TechLevel.Industrial];

                if (mouseOver)
                    GUI.DrawTexture(visibleRect, Assets.ButtonActive);
                else
                    GUI.DrawTexture(visibleRect, Assets.Button);

                var progressBarRect = visibleRect.ContractedBy(3f);
                GUI.color = Assets.ColorAvailable[TechLevel.Industrial];
                progressBarRect.xMin += (isActive ? 1f : 0f ) * progressBarRect.width;
                GUI.DrawTexture(progressBarRect, BaseContent.WhiteTex);

                GUI.color = Color.white;


                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = false;
                Text.Font = visibleRect.width > 100?GameFont.Small:GameFont.Tiny;
                Widgets.Label(visibleRect, TabLabel);

            }

            var btn = Widgets.ButtonInvisible(visibleRect);
            if (btn)
                // LMB is queue operations, RMB is info
                if (Event.current.button == 0)
                {
                    ActiveTree = Trees[TabName];
                    ActiveTree.Initialize();
                    ActiveTree.OrderDirty = true;
                    
                }
        }

        public Tree (Tree loadedTree)
        {
            _edges = loadedTree.Edges;
            _nodes = loadedTree.Nodes;

            _relevantTechLevels = loadedTree._relevantTechLevels;
            _techLevelBounds = loadedTree._techLevelBounds;
            Initialized = loadedTree.Initialized;
            OrderDirty = loadedTree.OrderDirty;
            Size = loadedTree.Size;
            TabName = loadedTree.TabName;
            TabLabel = loadedTree.TabLabel;
            _treeRect = loadedTree._treeRect;

            Trees[TabName] = this;
        }

//        [SyncMethod]
        public void Initialize()
        {
            if (Initialized) return;

            // make sure we only have one initializer running
            if (_initializing)
                return;
            _initializing = true;

            if (CacheIO.CacheShouldBeLoaded)
            {
                Log.Message("Loading from Cache");
                var tree = CacheIO.LoadFromCache(TabName);
                if (tree != null)
                {
                    if (tree != ActiveTree)
                    { 
                        ActiveTree = new Tree(tree);
                        ActiveTree.Initialized = true;

                        LongEventHandler.QueueLongEvent(Tabbed_MainTabWindow_ResearchTree.Instance.Notify_TreeInitialized,
                            "Fluffy.ResearchTree.RestoreQueue." + TabName, false, null);
                        return;

                    }
                }

            }

            // setup
            LongEventHandler.QueueLongEvent(CheckPrerequisites, "Fluffy.ResearchTree.PreparingTree.Setup." + TabName, false,
                null);
            LongEventHandler.QueueLongEvent(CreateEdges, "Fluffy.ResearchTree.PreparingTree.Setup." + TabName, false, null);
            LongEventHandler.QueueLongEvent(HorizontalPositions, "Fluffy.ResearchTree.PreparingTree.Setup." + TabName, false,
                null);
            LongEventHandler.QueueLongEvent(NormalizeEdges, "Fluffy.ResearchTree.PreparingTree.Setup." + TabName, false, null);
#if DEBUG
            LongEventHandler.QueueLongEvent(DebugStatus, "Fluffy.ResearchTree.PreparingTree.Setup." + TabName, false, null);
#endif

            // crossing reduction
            LongEventHandler.QueueLongEvent(Collapse, "Fluffy.ResearchTree.PreparingTree.CrossingReduction." + TabName, false,
                null);
            LongEventHandler.QueueLongEvent(MinimizeCrossings, "Fluffy.ResearchTree.PreparingTree.CrossingReduction." + TabName,
                false, null);
#if DEBUG
            LongEventHandler.QueueLongEvent(DebugStatus, "Fluffy.ResearchTree.PreparingTree.CrossingReduction." + TabName, false,
                null);
#endif

            // layout
            LongEventHandler.QueueLongEvent(MinimizeEdgeLength, "Fluffy.ResearchTree.PreparingTree.Layout." + TabName, false,
                null);
            LongEventHandler.QueueLongEvent(RemoveEmptyRows, "Fluffy.ResearchTree.PreparingTree.Layout." + TabName, false, null);
#if DEBUG
            LongEventHandler.QueueLongEvent(DebugStatus, "Fluffy.ResearchTree.PreparingTree.Layout." + TabName, false, null);
#endif

            // done!
            LongEventHandler.QueueLongEvent(() => { Initialized = true; CacheIO.SaveTab(this);}, "Fluffy.ResearchTree.PreparingTree.Layout." + TabName,
                false, null);

            // tell research tab we're ready
            LongEventHandler.QueueLongEvent(Tabbed_MainTabWindow_ResearchTree.Instance.Notify_TreeInitialized,
                "Fluffy.ResearchTree.RestoreQueue." + TabName, false, null);

        }

      

        private void RemoveEmptyRows()
        {
            Log.Debug("Removing empty rows");
            Profiler.Start();
            var y = 1;
            while (y <= Size.z)
            {
                var row = Row(y);
                if (row.NullOrEmpty())
                    foreach (var node in Nodes.Where(n => n.Y > y))
                        node.Y--;
                else
                    y++;
            }

            Profiler.End();
        }

        private void MinimizeEdgeLength()
        {
            Log.Debug("Minimize edge length.");
            Profiler.Start();

            // move and/or swap nodes to reduce the total edge length
            // perform sweeps of adjacent node reorderings
            var progress = false;
            int iteration = 0, burnout = 2, max_iterations = 50;
            while ((!progress || burnout > 0) && iteration < max_iterations)
            {
                progress = EdgeLengthSweep_Local(iteration++);
                if (!progress)
                    burnout--;
            }

            // sweep until we had no progress 2 times, then keep sweeping until we had progress
            iteration = 0;
            burnout = 2;
            while (burnout > 0 && iteration < max_iterations)
            {
                progress = EdgeLengthSweep_Global(iteration++);
                if (!progress)
                    burnout--;
            }

            Profiler.End();
        }

        private bool EdgeLengthSweep_Global(int iteration)
        {
            Profiler.Start("iteration" + iteration);
            // calculate edge length before sweep
            var before = EdgeLength();

            // do left/right sweep, align with left/right nodes for 4 different iterations.
            //if (iteration % 2 == 0)
            for (var l = 2; l <= Size.x; l++)
                EdgeLengthSweep_Global_Layer(l, true);
            //else
            //    for (var l = 1; l < Size.x; l++)
            //        EdgeLengthSweep_Global_Layer(l, false);

            // calculate edge length after sweep
            var after = EdgeLength();

            // return progress
            Log.Debug($"EdgeLengthSweep_Global, iteration {iteration}: {before} -> {after}");
            Profiler.End();
            return after < before;
        }


        private bool EdgeLengthSweep_Local(int iteration)
        {
            Profiler.Start("iteration" + iteration);
            // calculate edge length before sweep
            var before = EdgeLength();

            // do left/right sweep, align with left/right nodes for 4 different iterations.
            if (iteration % 2 == 0)
                for (var l = 2; l <= Size.x; l++)
                    EdgeLengthSweep_Local_Layer(l, true);
            else
                for (var l = Size.x - 1; l >= 0; l--)
                    EdgeLengthSweep_Local_Layer(l, false);

            // calculate edge length after sweep
            var after = EdgeLength();

            // return progress
            Log.Debug($"EdgeLengthSweep_Local, iteration {iteration}: {before} -> {after}");
            Profiler.End();
            return after < before;
        }

        private void EdgeLengthSweep_Global_Layer(int l, bool @in)
        {
            // The objective here is to;
            // (1) move and/or swap nodes to reduce total edge length
            // (2) not increase the number of crossings

            var length = EdgeLength(l, @in);
            var crossings = Crossings(l);
            if (Math.Abs(length) < Epsilon)
                return;

            var layer = Layer(l, true);
            foreach (var node in layer)
            {
                // we only need to loop over positions that might be better for this node.
                // min = minimum of current position, minimum of any connected nodes current position
                var neighbours = node.Nodes;
                if (!neighbours.Any())
                    continue;

                var min = Mathf.Min(node.Y, neighbours.Min(n => n.Y));
                var max = Mathf.Max(node.Y, neighbours.Max(n => n.Y));
                if (min == max && min == node.Y)
                    continue;

                for (var y = min; y <= max; y++)
                {
                    if (y == node.Y)
                        continue;

                    // is this spot occupied? 
                    var otherNode = NodeAt(l, y);

                    // occupied, try swapping
                    if (otherNode != null)
                    {
                        Swap(node, otherNode);
                        var candidateCrossings = Crossings(l);
                        if (candidateCrossings > crossings)
                        {
                            // abort
                            Swap(otherNode, node);
                        }
                        else
                        {
                            var candidateLength = EdgeLength(l, @in);
                            if (length - candidateLength < Epsilon)
                            {
                                // abort
                                Swap(otherNode, node);
                            }
                            else
                            {
                                Log.Trace("\tSwapping {0} and {1}: {2} -> {3}", node, otherNode, length,
                                    candidateLength);
                                length = candidateLength;
                            }
                        }
                    }

                    // not occupied, try moving
                    else
                    {
                        var oldY = node.Y;
                        node.Y = y;
                        var candidateCrossings = Crossings(l);
                        if (candidateCrossings > crossings)
                        {
                            // abort
                            node.Y = oldY;
                        }
                        else
                        {
                            var candidateLength = EdgeLength(l, @in);
                            if (length - candidateLength < Epsilon)
                            {
                                // abort
                                node.Y = oldY;
                            }
                            else
                            {
                                Log.Trace("\tMoving {0} -> {1}: {2} -> {3}", node, new Vector2(node.X, oldY), length,
                                    candidateLength);
                                length = candidateLength;
                            }
                        }
                    }
                }
            }
        }


        private void EdgeLengthSweep_Local_Layer(int l, bool @in)
        {
            // The objective here is to;
            // (1) move and/or swap nodes to reduce local edge length
            // (2) not increase the number of crossings
            var x = @in ? l - 1 : l + 1;
            var crossings = Crossings(x);

            var layer = Layer(l, true);
            foreach (var node in layer)
            foreach (var edge in @in ? node.InEdges : node.OutEdges)
            {
                // current length
                var length = edge.Length;
                var neighbour = @in ? edge.In : edge.Out;
                if (neighbour.X != x)
                    Log.Warning("{0} is not at layer {1}", neighbour, x);

                // we only need to loop over positions that might be better for this node.
                // min = minimum of current position, node position
                var min = Mathf.Min(node.Y, neighbour.Y);
                var max = Mathf.Max(node.Y, neighbour.Y);

                // already at only possible position
                if (min == max && min == node.Y)
                    continue;

                for (var y = min; y <= max; y++)
                {
                    if (y == neighbour.Y)
                        continue;

                    // is this spot occupied? 
                    var otherNode = NodeAt(x, y);

                    // occupied, try swapping
                    if (otherNode != null)
                    {
                        Swap(neighbour, otherNode);
                        var candidateCrossings = Crossings(x);
                        if (candidateCrossings > crossings)
                        {
                            // abort
                            Swap(otherNode, neighbour);
                        }
                        else
                        {
                            var candidateLength = edge.Length;
                            if (length - candidateLength < Epsilon)
                            {
                                // abort
                                Swap(otherNode, neighbour);
                            }
                            else
                            {
                                Log.Trace("\tSwapping {0} and {1}: {2} -> {3}", neighbour, otherNode, length,
                                    candidateLength);
                                length = candidateLength;
                            }
                        }
                    }

                    // not occupied, try moving
                    else
                    {
                        var oldY = neighbour.Y;
                        neighbour.Y = y;
                        var candidateCrossings = Crossings(x);
                        if (candidateCrossings > crossings)
                        {
                            // abort
                            neighbour.Y = oldY;
                        }
                        else
                        {
                            var candidateLength = edge.Length;
                            if (length - candidateLength < Epsilon)
                            {
                                // abort
                                neighbour.Y = oldY;
                            }
                            else
                            {
                                Log.Trace("\tMoving {0} -> {1}: {2} -> {3}", neighbour,
                                    new Vector2(neighbour.X, oldY), length, candidateLength);
                                length = candidateLength;
                            }
                        }
                    }
                }
            }
        }

        public void HorizontalPositions()
        {
            // get list of techlevels
            var techlevels = RelevantTechLevels;
            bool anyChange;
            var iteration = 1;
            var maxIterations = 50;

            Log.Debug("Assigning horizontal positions.");
            Profiler.Start();

            // assign horizontal positions based on tech levels and prerequisites
            do
            {
                Profiler.Start("iteration " + iteration);
                var min = 1;
                anyChange = false;

                foreach (var techlevel in techlevels)
                {
                    // enforce minimum x position based on techlevels
                    var nodes = Nodes.OfType<ResearchNode>().Where(n => n.Research.techLevel == techlevel);
                    if (!nodes.Any())
                        continue;

                    foreach (var node in nodes)
                        anyChange = node.SetDepth(min) || anyChange;

                    min = nodes.Max(n => n.X) + 1;

                    Log.Trace("\t{0}, change: {1}", techlevel, anyChange);
                }

                Profiler.End();
            } while (anyChange && iteration++ < maxIterations);


            // store tech level boundaries
            _techLevelBounds = new Dictionary<TechLevel, IntRange>();
            foreach (var techlevel in techlevels)
            {
                var nodes = Nodes.OfType<ResearchNode>().Where(n => n.Research.techLevel == techlevel);
                _techLevelBounds[techlevel] = new IntRange(nodes.Min(n => n.X) - 1, nodes.Max(n => n.X));
            }

            Profiler.End();
        }

        private void NormalizeEdges()
        {
            Log.Debug("Normalizing edges.");
            Profiler.Start();
            foreach (var edge in new List<Edge<Node, Node>>(Edges.Where(e => e.Span > 1)))
            {
                Log.Trace("\tCreating dummy chain for {0}", edge);

                // remove and decouple long edge
                Edges.Remove(edge);
                edge.In.OutEdges.Remove(edge);
                edge.Out.InEdges.Remove(edge);
                var cur = edge.In;
                var yOffset = (edge.Out.Yf - edge.In.Yf) / edge.Span;

                // create and hook up dummy chain
                for (var x = edge.In.X + 1; x < edge.Out.X; x++)
                {
                    var dummy = new DummyNode();
                    dummy.X = x;
                    dummy.Yf = edge.In.Yf + yOffset * (x - edge.In.X);
                    var dummyEdge = new Edge<Node, Node>(cur, dummy);
                    cur.OutEdges.Add(dummyEdge);
                    dummy.InEdges.Add(dummyEdge);
                    _nodes.Add(dummy);
                    Edges.Add(dummyEdge);
                    cur = dummy;
                    Log.Trace("\t\tCreated dummy {0}", dummy);
                }

                // hook up final dummy to out node
                var finalEdge = new Edge<Node, Node>(cur, edge.Out);
                cur.OutEdges.Add(finalEdge);
                edge.Out.InEdges.Add(finalEdge);
                Edges.Add(finalEdge);
            }

            Profiler.End();
        }

        private void CreateEdges()
        {
            Log.Debug("Creating edges.");
            Profiler.Start();
            // create links between nodes
            if (_edges.NullOrEmpty()) _edges = new List<Edge<Node, Node>>();

            foreach (var node in Nodes.OfType<ResearchNode>())
            {
                if (node.Research.prerequisites.NullOrEmpty())
                    continue;
                foreach (var prerequisite in node.Research.prerequisites)
                {
                    ResearchNode prerequisiteNode = prerequisite;
                    if (prerequisiteNode == null)
                        continue;
                    var edge = new Edge<Node, Node>(prerequisiteNode, node);
                    Edges.Add(edge);
                    node.InEdges.Add(edge);
                    prerequisiteNode.OutEdges.Add(edge);
                    Log.Trace("\tCreated edge {0}", edge);
                }
            }

            Profiler.End();
        }

        private void CheckPrerequisites()
        {
            // check prerequisites
            Log.Debug("Checking prerequisites.");
            Profiler.Start();

            var nodes = new Queue<ResearchNode>(Nodes.OfType<ResearchNode>());
            // remove redundant prerequisites
            while (nodes.Count > 0)
            {
                var node = nodes.Dequeue();
                if (node.Research.prerequisites.NullOrEmpty())
                    continue;

                var ancestors = node.Research.prerequisites?.SelectMany(r => r.Ancestors()).ToList();
                var redundant = ancestors.Intersect(node.Research.prerequisites);
                if (redundant.Any())
                {
                    Log.Warning("\tredundant prerequisites for {0}: {1}", node.Research.LabelCap,
                        string.Join(", ", redundant.Select(r => r.LabelCap).ToArray()));
                    foreach (var redundantPrerequisite in redundant)
                        node.Research.prerequisites.Remove(redundantPrerequisite);
                }
            }

            // fix bad techlevels
            nodes = new Queue<ResearchNode>(Nodes.OfType<ResearchNode>());
            while (nodes.Count > 0)
            {
                var node = nodes.Dequeue();
                if (!node.Research.prerequisites.NullOrEmpty())
                    // warn and fix badly configured techlevels
                    if (node.Research.prerequisites.Any(r => r.techLevel > node.Research.techLevel))
                    {
                        Log.Warning("\t{0} has a lower techlevel than (one of) it's prerequisites",
                            node.Research.defName);
                        node.Research.techLevel = node.Research.prerequisites.Max(r => r.techLevel);

                        // re-enqeue all descendants
                        foreach (var descendant in node.Descendants.OfType<ResearchNode>())
                            nodes.Enqueue(descendant);
                    }
            }

            Profiler.End();
        }

        private void PopulateNodes()
        {
            Log.Debug("Populating nodes.");
            Profiler.Start();

            var projects = DefDatabase<ResearchProjectDef>.AllDefsListForReading;

            // find hidden nodes (nodes that have themselves as a prerequisite)
            var hidden = projects.Where(p => p.prerequisites?.Contains(p) ?? false);

            // find locked nodes (nodes that have a hidden node as a prerequisite)
            var locked = projects.Where(p => p.Ancestors().Intersect(hidden).Any());
            if (TabName == ALL_TAB_NAME)
            {
                _nodes = new List<Node>(DefDatabase<ResearchProjectDef>.AllDefsListForReading
//                    .Where(def => def.tab.defName == TabName)
                    .Except(hidden)
                    .Except(locked)
                    .Select(def => new ResearchNode(def) as Node));
                _nodes.RemoveAll(n => n == null);

            }
            else
            {
                // populate all nodes
                _nodes = new List<Node>(DefDatabase<ResearchProjectDef>.AllDefsListForReading
                .Where(def => def.tab.defName == TabName)
                    .Except(hidden)
                    .Except(locked)
                    .Select(def => new ResearchNode(def) as Node));

                _nodes.RemoveAll(n => n == null);

                List<Node> nodesToAdd = new List<Node>();
                List<ResearchProjectDef> addedDefs = new List<ResearchProjectDef>();
                foreach (var node in _nodes)
                {
                    var missedNodes = (node as ResearchNode).Research.Ancestors().Where(n => n.tab.defName != TabName);
                
                    foreach (var researchProjectDef in missedNodes.Except(hidden).Except(locked).Except(addedDefs))
                    {
                        Verse.Log.Message("NameDef");
                        Verse.Log.Message(researchProjectDef.defName);
                        Verse.Log.Message("Tree");
                        Verse.Log.Message(Trees[researchProjectDef.tab.defName].TabName);
                        Verse.Log.Message("Nodes");
                        Verse.Log.Message(Trees[researchProjectDef.tab.defName].Nodes.Count.ToString());
                        Verse.Log.Message("Select");
                        var s = Trees[researchProjectDef.tab.defName].Nodes.Select(n => n as ResearchNode);
                        var l = s.ToList();
                        Verse.Log.Message("Exist");
                        Verse.Log.Message(l[0].Research.defName);
                        Verse.Log.Message(l.Exists(n => n != null && n.Research != null && n.Research.defName == researchProjectDef.defName) ? "true" : "false");
                        Verse.Log.Message("Find");
                        var r = l.Find(n => n != null && n.Research != null && n.Research.defName == researchProjectDef.defName);
                        Verse.Log.Message("Fake");
                
                        nodesToAdd.Add(new FakeResearchNode(r)); //.Find(n=>n.r)
                    }
                
                    addedDefs.AddRange(missedNodes);
                }
                
                _nodes.AddRange(nodesToAdd.Distinct());
                _nodes.RemoveAll(n => n == null);

            }

          
            //foreach (var node in _nodes )
            //{
            //    var missingNodes = (node as ResearchNode).GetMissingRequiredRecursive().Select((researchNode) => researchNode.Research);
            //        _nodes.AddRange(missingNodes                    
            //        .Except(hidden)
            //        .Except(locked)
            //        .Select(def => new ResearchNode(def) as Node));
            //    ;

            //}

            Log.Debug("\t{0} nodes", _nodes.Count);
            Profiler.End();
        }

        private void Collapse()
        {
            Log.Debug("Collapsing nodes.");
            Profiler.Start();
            var pre = Size;
            for (var l = 1; l <= Size.x; l++)
            {
                var nodes = Layer(l, true);
                var Y = 1;
                foreach (var node in nodes)
                    node.Y = Y++;
            }

            Log.Debug("{0} -> {1}", pre, Size);
            Profiler.End();
        }

        //[Conditional("DEBUG")]
        //internal static void DebugDraw()
        //{
        //    foreach (var v in ActiveTree.Nodes)
        //    foreach (var w in v.OutNodes)
        //        Widgets.DrawLine(v.Right, w.Left, Color.white, 1);
        //}


        public void Draw(Rect visibleRect)
        {
            Profiler.Start("Tree.Draw");
            Profiler.Start("techlevels");
            foreach (var techlevel in RelevantTechLevels)
                DrawTechLevel(techlevel, visibleRect);
            Profiler.End();

            Profiler.Start("edges");
            foreach (var edge in Edges.OrderBy(e => e.DrawOrder))
                edge.Draw(visibleRect);
            Profiler.End();

            Profiler.Start("nodes");
            foreach (var node in Nodes)
                node.Draw(visibleRect);
            Profiler.End();
        }

        public void DrawTechLevel(TechLevel techlevel, Rect visibleRect)
        {
            if (!TechLevelBounds.ContainsKey(techlevel)) return;

            // determine positions
            var xMin = (NodeSize.x + NodeMargins.x) * TechLevelBounds[techlevel].min - NodeMargins.x / 2f;
            var xMax = (NodeSize.x + NodeMargins.x) * TechLevelBounds[techlevel].max - NodeMargins.x / 2f;

            GUI.color = Assets.TechLevelColor;
            Text.Anchor = TextAnchor.MiddleCenter;

            // lower bound
            if (TechLevelBounds[techlevel].min > 0 && xMin > visibleRect.xMin && xMin < visibleRect.xMax)
            {
                // line
                Widgets.DrawLine(new Vector2(xMin, visibleRect.yMin), new Vector2(xMin, visibleRect.yMax),
                    Assets.TechLevelColor, 1f);

                // label
                var labelRect = new Rect(
                    xMin + TechLevelLabelSize.y / 2f - TechLevelLabelSize.x / 2f,
                    visibleRect.center.y - TechLevelLabelSize.y / 2f,
                    TechLevelLabelSize.x,
                    TechLevelLabelSize.y);

                VerticalLabel(labelRect, techlevel.ToStringHuman());
            }

            // upper bound
            if (TechLevelBounds[techlevel].max < Size.x && xMax > visibleRect.xMin && xMax < visibleRect.xMax)
            {
                // label
                var labelRect = new Rect(
                    xMax - TechLevelLabelSize.y / 2f - TechLevelLabelSize.x / 2f,
                    visibleRect.center.y - TechLevelLabelSize.y / 2f,
                    TechLevelLabelSize.x,
                    TechLevelLabelSize.y);

                VerticalLabel(labelRect, techlevel.ToStringHuman());
            }

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void VerticalLabel(Rect rect, string text)
        {
            // store the scaling matrix
            var matrix = GUI.matrix;

            // rotate and then apply the scaling
            GUI.matrix = Matrix4x4.identity;
            GUIUtility.RotateAroundPivot(-90f, rect.center);
            GUI.matrix = matrix * GUI.matrix;

            Widgets.Label(rect, text);

            // restore the original scaling matrix
            GUI.matrix = matrix;
        }

        private Node NodeAt(int X, int Y)
        {
            return Nodes.FirstOrDefault(n => n.X == X && n.Y == Y);
        }

        public void MinimizeCrossings()
        {
            // initialize each layer by putting nodes with the most (recursive!) children on bottom
            Log.Debug("Minimize crossings.");
            Profiler.Start();

            for (var X = 1; X <= Size.x; X++)
            {
                var nodes = Layer(X).OrderBy(n => n.Descendants.Count).ToList();
                for (var i = 0; i < nodes.Count; i++)
                    nodes[i].Y = i + 1;
            }

            // up-down sweeps of mean reordering
            var progress = false;
            int iteration = 0, burnout = 2, max_iterations = 50;
            while ((!progress || burnout > 0) && iteration < max_iterations)
            {
                progress = BarymetricSweep(iteration++);
                if (!progress)
                    burnout--;
            }

            // greedy sweep for local optima
            iteration = 0;
            burnout = 2;
            while (burnout > 0 && iteration < max_iterations)
            {
                progress = GreedySweep(iteration++);
                if (!progress)
                    burnout--;
            }

            Profiler.End();
        }

        private bool GreedySweep(int iteration)
        {
            Profiler.Start("iteration " + iteration);

            // count number of crossings before sweep
            var before = Crossings();

            // do up/down sweep on aternating iterations
            if (iteration % 2 == 0)
                for (var l = 1; l <= Size.x; l++)
                    GreedySweep_Layer(l);
            else
                for (var l = Size.x; l >= 1; l--)
                    GreedySweep_Layer(l);

            // count number of crossings after sweep
            var after = Crossings();

            Log.Debug($"GreedySweep: {before} -> {after}");
            Profiler.End();

            // return progress
            return after < before;
        }

        private void GreedySweep_Layer(int l)
        {
            // The objective here is twofold;
            // 1: Swap nodes to reduce the number of crossings
            // 2: Swap nodes so that inner edges (edges between dummies)
            //    avoid crossings at all costs.
            //
            // If I'm reasoning this out right, both objectives should be served by
            // minimizing the amount of crossings between each pair of nodes.
            var crossings = Crossings(l);
            if (crossings == 0)
                return;

            var layer = Layer(l, true);
            for (var i = 0; i < layer.Count - 1; i++)
            for (var j = i + 1; j < layer.Count; j++)
            {
                // swap, then count crossings again. If lower, leave it. If higher, revert.
                Swap(layer[i], layer[j]);
                var candidateCrossings = Crossings(l);
                if (candidateCrossings < crossings)
                    // update current crossings
                    crossings = candidateCrossings;
                else
                    // revert change
                    Swap(layer[j], layer[i]);
            }
        }

        private void Swap(Node A, Node B)
        {
            if (A.X != B.X)
                throw new Exception("Can't swap nodes on different layers");

            // swap Y positions of adjacent nodes
            var tmp = A.Y;
            A.Y = B.Y;
            B.Y = tmp;
        }

        private bool BarymetricSweep(int iteration)
        {
            Profiler.Start("iteration " + iteration);

            // count number of crossings before sweep
            var before = Crossings();

            // do up/down sweep on alternating iterations
            if (iteration % 2 == 0)
                for (var i = 2; i <= Size.x; i++)
                    BarymetricSweep_Layer(i, true);
            else
                for (var i = Size.x - 1; i > 0; i--)
                    BarymetricSweep_Layer(i, false);

            // count number of crossings after sweep
            var after = Crossings();

            // did we make progress? please?
            Log.Debug(
                $"BarymetricSweep {iteration} ({(iteration % 2 == 0 ? "left" : "right")}): {before} -> {after}");
            Profiler.End();
            return after < before;
        }

        private void BarymetricSweep_Layer(int layer, bool left)
        {
            var means = Layer(layer)
                .ToDictionary(n => n, n => GetBarycentre(n, left ? n.InNodes : n.OutNodes))
                .OrderBy(n => n.Value);

            // create groups of nodes at similar means
            var cur = float.MinValue;
            var groups = new Dictionary<float, List<Node>>();
            foreach (var mean in means)
            {
                if (Math.Abs(mean.Value - cur) > Epsilon)
                {
                    cur = mean.Value;
                    groups[cur] = new List<Node>();
                }

                groups[cur].Add(mean.Key);
            }

            // position nodes as close to their desired mean as possible
            var Y = 1;
            foreach (var group in groups)
            {
                var mean = group.Key;
                var N = group.Value.Count;
                Y = (int) Mathf.Max(Y, mean - (N - 1) / 2);

                foreach (var node in group.Value)
                    node.Y = Y++;
            }
        }

        private float GetBarycentre(Node node, List<Node> neighbours)
        {
            if (neighbours.NullOrEmpty())
                return node.Yf;

            return neighbours.Sum(n => n.Yf) / neighbours.Count;
        }

        private int Crossings()
        {
            var crossings = 0;
            for (var layer = 1; layer < Size.x; layer++) crossings += Crossings(layer, true);
            return crossings;
        }

        private float EdgeLength()
        {
            var length = 0f;
            for (var layer = 1; layer < Size.x; layer++) length += EdgeLength(layer, true);
            return length;
        }

        private int Crossings(int layer)
        {
            if (layer == 0)
                return Crossings(layer, false);
            if (layer == Size.x)
                return Crossings(layer, true);
            return Crossings(layer, true) + Crossings(layer, false);
        }

        private float EdgeLength(int layer)
        {
            if (layer == 0)
                return EdgeLength(layer, false);
            if (layer == Size.x)
                return EdgeLength(layer, true);
            return EdgeLength(layer, true) *
                   EdgeLength(layer, false); // multply to favor moving nodes closer to one endpoint
        }

        private int Crossings(int layer, bool @in)
        {
            // get in/out edges for layer
            var edges = Layer(layer)
                .SelectMany(n => @in ? n.InEdges : n.OutEdges)
                .OrderBy(e => e.In.Y)
                .ThenBy(e => e.Out.Y)
                .ToList();

            if (edges.Count < 2)
                return 0;

            // count number of inversions
            var inversions = 0;
            for (var i = 0; i < edges.Count - 1; i++)
            for (var j = i + 1; j < edges.Count; j++)
                if (edges[j].Out.Y < edges[i].Out.Y)
                    inversions++;

            return inversions;
        }

        private float EdgeLength(int layer, bool @in)
        {
            // get in/out edges for layer
            var edges = Layer(layer)
                .SelectMany(n => @in ? n.InEdges : n.OutEdges)
                .OrderBy(e => e.In.Y)
                .ThenBy(e => e.Out.Y)
                .ToList();

            if (edges.NullOrEmpty())
                return 0f;

            return edges.Sum(e => e.Length) * (@in ? 2 : 1);
        }

        public List<Node> Layer(int depth, bool ordered = false)
        {
            if (ordered && OrderDirty)
            {
                _nodes = Nodes.OrderBy(n => n.X).ThenBy(n => n.Y).ToList();
                OrderDirty = false;
            }

            return Nodes.Where(n => n.X == depth).ToList();
        }

        public List<Node> Row(int Y)
        {
            return Nodes.Where(n => n.Y == Y).ToList();
        }

        public new string ToString()
        {
            var text = new StringBuilder();

            for (var l = 1; l <= Nodes.Max(n => n.X); l++)
            {
                text.AppendLine($"Layer {l}:");
                var layer = Layer(l, true);

                foreach (var n in layer)
                {
                    text.AppendLine($"\t{n}");
                    text.AppendLine("\t\tAbove: " +
                                    string.Join(", ", n.InNodes.Select(a => a.ToString()).ToArray()));
                    text.AppendLine("\t\tBelow: " +
                                    string.Join(", ", n.OutNodes.Select(b => b.ToString()).ToArray()));
                }
            }

            return text.ToString();
        }

        public void DebugStatus()
        {
            Log.Message("duplicated positions:\n " +
                        string.Join(
                            "\n",
                            Nodes.Where(n => Nodes.Any(n2 => n != n2 && n.X == n2.X && n.Y == n2.Y))
                                .Select(n => n.X + ", " + n.Y + ": " + n.Label).ToArray()));
            Log.Message("out-of-bounds nodes:\n" +
                        string.Join(
                            "\n", Nodes.Where(n => n.X < 1 || n.Y < 1).Select(n => n.ToString()).ToArray()));
            Log.Trace(ToString());
        }
    }
}