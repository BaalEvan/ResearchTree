﻿// Queue.cs
// Copyright Karel Kroeze, 2020-2020

//using Multiplayer.API;

using static FluffyResearchTree.Assets;
using static FluffyResearchTree.Constants;

namespace FluffyResearchTree
{
    using System.Collections.Generic;
    using System.Linq;
    using RimWorld;
    using RimWorld.Planet;
    using UnityEngine;
    using Verse;

    public class Queue : WorldComponent
    {
        private static Queue _instance;
        private readonly List<ResearchNode> _queue = new List<ResearchNode>();
        private List<ResearchProjectDef> _saveableQueue;

        public Queue(World world) : base(world)
        {
            _instance = this;
        }

        /// <summary>
        ///     Removes and returns the first node in the queue.
        /// </summary>
        /// <returns></returns>
        public static ResearchNode Pop
        {
            get
            {
                if (_instance._queue != null && _instance._queue.Count > 0)
                {
                    var node = _instance._queue[0];
                    _instance._queue.RemoveAt(0);
                    return node;
                }

                return null;
            }
        }

        public static int NumQueued => _instance._queue.Count - 1;

        public static void TryDequeue(ResearchNode node)
        {
            if (_instance._queue.Contains(node))
                Dequeue(node);
        }

        public static bool TryToMove(ResearchNode node)
        {
            var dropPosition = Event.current.mousePosition;
            // Check if Mouse is Outside node
            if (node.RealQueueRect.Contains(dropPosition)) return false;

            // Order Queue by distance to mouse 
            var queueByDistance = _instance._queue.OrderBy(item => Vector2.Distance(item.RealQueueRect.center, dropPosition));
            var nearestItem = queueByDistance.First();
            var indexToPlace = _instance._queue.IndexOf(nearestItem);

            // Move node to new Position
            _instance._queue.Remove(node);
            _instance._queue.Insert(indexToPlace, node);

            // Sort nodes required to complete this node
            SortRequiredRecursive(node);

            // Sort nodes which require this node to complete
            var children = node.Research.Descendants();
            if (children != null && children.Count > 0)
            {
                var childrenNodes = children
                    .Where(def => !def.IsFinished && IsQueued(def))
                    .Select(def => def.ResearchNodeForQueue()).ToList();
                foreach (var child in childrenNodes) SortRequiredRecursive(child);
            }

            return true;
        }

        private static void SortRequiredRecursive(ResearchNode node)
        {
            var indexToPlace = _instance._queue.IndexOf(node);
            var requiredResearchNodes = node.GetMissingRequiredRecursiveFromAll().ToList();
            foreach (var requiredResearchNode in requiredResearchNodes)
                if (IsQueued(requiredResearchNode))
                {
                    var requiredNodeIndex = _instance._queue.IndexOf(requiredResearchNode);
                    if (requiredNodeIndex > indexToPlace)
                    {
                        _instance._queue.Remove(requiredResearchNode);
                        _instance._queue.Insert(indexToPlace, requiredResearchNode);
                        SortRequiredRecursive(requiredResearchNode);
                    }
                }
        }

//        [SyncMethod]
        public static void Dequeue(ResearchNode node)
        {
            // remove this node
            _instance._queue.Remove(node);

            // remove all nodes that depend on it
            var followUps = _instance._queue.Where(n => n.GetMissingRequiredRecursiveFromAll().Contains(node)).ToList();
            foreach (var followUp in followUps)
                _instance._queue.Remove(followUp);

            // if currently researching this node, stop that
            if (Find.ResearchManager.currentProj == node.Research)
                Find.ResearchManager.currentProj = null;
        }

        public static void DrawLabels(Rect visibleRect)
        {
            Profiler.Start("Queue.DrawLabels");
            var i = 1;
            foreach (var node in _instance._queue)
            {
                if (node.IsVisible(visibleRect))
                {
                    var main = ColorCompleted[node.Research.techLevel];
                    var background = i > 1 ? ColorUnavailable[node.Research.techLevel] : main;
                    DrawLabel(node.QueueRect, main, background, i);
                }

                foreach (var fakeResearchNode in node.fakeLinks)
                {
                    if (fakeResearchNode.IsVisible(visibleRect))
                    {
                        var main = ColorCompleted[node.Research.techLevel];
                        var background = i > 1 ? ColorUnavailable[node.Research.techLevel] : main;
                        DrawLabel(fakeResearchNode.QueueRect, main, background, i);
                    }
                }

                i++;
            }

            Profiler.End();
        }

        public static void DrawLabel(Rect canvas, Color main, Color background, int label)
        {
            // draw coloured tag
            GUI.color = main;
            GUI.DrawTexture(canvas, CircleFill);

            // if this is not first in line, grey out centre of tag
            if (background != main)
            {
                GUI.color = background;
                GUI.DrawTexture(canvas.ContractedBy(2f), CircleFill);
            }

            // draw queue number
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(canvas, label.ToString());
            Text.Anchor = TextAnchor.UpperLeft;
        }

        public static void Enqueue(ResearchNode node, bool add)
        {
            Log.Debug($"Enqueuing: {node.Research.defName}");

            // if we're not adding, clear the current queue and current research project
            if (!add)
            {
                _instance._queue.Clear();
                Find.ResearchManager.currentProj = null;
            }

            // add to the queue if not already in it
            if (!_instance._queue.Contains(node))
                _instance._queue.Add(node);

            // try set the first research in the queue to be the current project.
            var next = _instance._queue.First();
            Find.ResearchManager.currentProj = next?.Research; // null if next is null.
        }

        public static void InsertAtBeginning(ResearchNode node)
        {
            Log.Debug($"Enqueuing: {node.Research.defName}");
            
            // add to the queue if not already in it
            if (!_instance._queue.Contains(node))
                _instance._queue.Insert(0, node);
            else
            {
                // move to the beginning if already on queue
                _instance._queue.Remove(node);
                _instance._queue.Insert(0, node);
            }
            // try set the first research in the queue to be the current project.
            var next = _instance._queue.First();
            Find.ResearchManager.currentProj = next?.Research; // null if next is null.
        }

        public static void InsertAtBeginningRange(IEnumerable<ResearchNode> nodes)
        {
            TutorSystem.Notify_Event("StartResearchProject");

            foreach (var node in nodes.OrderByDescending(node => node.X).ThenBy(node => node.Research.CostApparent))
            {
                InsertAtBeginning(node);
            }
        }

//        [SyncMethod]
        public static void EnqueueRange(IEnumerable<ResearchNode> nodes, bool add)
        {
            TutorSystem.Notify_Event("StartResearchProject");

            // clear current Queue if not adding
            if (!add)
            {
                _instance._queue.Clear();
                Find.ResearchManager.currentProj = null;
            }

            // sorting by depth ensures prereqs are met - cost is just a bonus thingy.
            foreach (var node in nodes.OrderBy(node => node.X).ThenBy(node => node.Research.CostApparent)) Enqueue(node, true);
        }

        public static bool IsQueued(ResearchNode node)
        {
            return _instance._queue.Exists(queued => queued.Research == node.Research);
        }

        public static void TryStartNext(ResearchProjectDef finished)
        {
            var current = _instance._queue.FirstOrDefault()?.Research;
            Log.Debug("TryStartNext: current; {0}, finished; {1}", current, finished);
            if (finished != _instance._queue.FirstOrDefault()?.Research)
            {
                TryDequeue(finished);
                return;
            }

            _instance._queue.RemoveAt(0);
            var next = _instance._queue.FirstOrDefault()?.Research;
            Log.Debug("TryStartNext: next; {0}", next);
            Find.ResearchManager.currentProj = next;
            DoCompletionLetter(current, next);
        }

        private static void DoCompletionLetter(ResearchProjectDef current, ResearchProjectDef next)
        {
            // message
            string label = "ResearchFinished".Translate(current.LabelCap);
            string text = current.LabelCap + "\n\n" + current.description;

            if (next != null)
            {
                text += "\n\n" + "Fluffy.ResearchTree.NextInQueue".Translate(next.LabelCap);
                Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.PositiveEvent);
            }
            else
            {
                text += "\n\n" + "Fluffy.ResearchTree.NextInQueue".Translate("Fluffy.ResearchTree.None".Translate());
                Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.NeutralEvent);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();

            // store research defs as these are the defining elements
            if (Scribe.mode == LoadSaveMode.Saving)
                _saveableQueue = _queue.Select(node => node.Research).ToList();

            Scribe_Collections.Look(ref _saveableQueue, "Queue", LookMode.Def);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                // initialize the queue
                foreach (var research in _saveableQueue)
                {
                    // find a node that matches the research - or null if none found
                    var node = research.ResearchNodeForQueue();

                    // enqueue the node
                    if (node != null)
                    {
                        Log.Debug("Adding {0} to queue", node.Research.LabelCap);
                        Enqueue(node, true);
                    }
                    else
                    {
                        Log.Debug("Could not find node for {0}", research.LabelCap);
                    }
                }
        }

        public static void DrawQueue(Rect canvas, bool interactible)
        {
            Profiler.Start("Queue.DrawQueue");
            if (!_instance._queue.Any())
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = TechLevelColor;
                Widgets.Label(canvas, "Fluffy.ResearchTree.NothingQueued".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }

            var pos = canvas.min;
            for (var i = 0; i < _instance._queue.Count && pos.x + NodeSize.x < canvas.xMax; i++)
            {
                var node = _instance._queue[i];
                var rect = new Rect(
                    pos.x - Margin,
                    pos.y - Margin,
                    NodeSize.x + 2 * Margin,
                    NodeSize.y + 2 * Margin
                );
                node.RealQueueRect = rect;
                node.DrawAt(pos, node.RealQueueRect, true);
                if (interactible && Mouse.IsOver(rect))
                    MainTabWindow_ResearchTree.Instance.CenterOn(node);
                pos.x += NodeSize.x + Margin;
            }

            Profiler.End();
        }

        public static void Notify_InstantFinished()
        {
            foreach (var node in new List<ResearchNode>(_instance._queue))
                if (node.Research.IsFinished)
                    TryDequeue(node);

            Find.ResearchManager.currentProj = _instance._queue.FirstOrDefault()?.Research;
        }
    }
}