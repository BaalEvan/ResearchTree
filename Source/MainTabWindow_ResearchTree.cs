﻿// MainTabWindow_ResearchTree.cs
// Copyright Karel Kroeze, 2020-2020

using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using static FluffyResearchTree.Constants;

namespace FluffyResearchTree
{
    using System.IO;
    using System.Runtime.Serialization;
    using System.Runtime.Serialization.Formatters.Binary;
    using global::ResearchTree;

    public class MainTabWindow_ResearchTree : MainTabWindow
    {
        internal static Vector2 _scrollPosition = Vector2.zero;


        private Rect _baseViewRect;
        private Rect _baseViewRect_Inner;

        private bool    _dragging;
        private Vector2 _mousePosition = Vector2.zero;

        private string _query = "";
        private Rect   _viewRect;

        private Rect _viewRect_Inner;
        private bool _viewRect_InnerDirty = true;
        private bool _viewRectDirty       = true;

        private float _zoomLevel = 1f;

        private bool _showTabs;

        public MainTabWindow_ResearchTree()
        {
            closeOnClickedOutside = false;
            Instance              = this;
        }

        public static MainTabWindow_ResearchTree Instance { get; private set; }

        public float ScaledMargin => Constants.Margin * ZoomLevel / Prefs.UIScale;

        public float ZoomLevel
        {
            get => _zoomLevel;
            set
            {
                _zoomLevel           = Mathf.Clamp( value, 1f, MaxZoomLevel );
                _viewRectDirty       = true;
                _viewRect_InnerDirty = true;
            }
        }

        public Rect ViewRect
        {
            get
            {
                if ( _viewRectDirty )
                {
                    _viewRect = new Rect(
                        _baseViewRect.xMin   * ZoomLevel,
                        _baseViewRect.yMin   * ZoomLevel,
                        _baseViewRect.width  * ZoomLevel,
                        _baseViewRect.height * ZoomLevel
                    );
                    _viewRectDirty = false;
                }

                return _viewRect;
            }
        }

        public Rect ViewRect_Inner
        {
            get
            {
                if ( _viewRect_InnerDirty )
                {
                    _viewRect_Inner      = _viewRect.ContractedBy( Margin * ZoomLevel );
                    _viewRect_InnerDirty = false;
                }

                return _viewRect_Inner;
            }
        }


        public Rect VisibleRect =>
            new Rect(
                _scrollPosition.x,
                _scrollPosition.y,
                ViewRect_Inner.width,
                ViewRect_Inner.height );

        internal float MaxZoomLevel
        {
            get
            {
                // get the minimum zoom level at which the entire tree fits onto the screen, or a static maximum zoom level.
                var fitZoomLevel = Mathf.Max( Tree.ActiveTree.TreeRect.width  / _baseViewRect_Inner.width,
                    Tree.ActiveTree.TreeRect.height / _baseViewRect_Inner.height );
                return Mathf.Min( fitZoomLevel, AbsoluteMaxZoomLevel );
            }
        }

        public override void PreClose()
        {
            base.PreClose();
            Log.Debug( "CloseOnClickedOutside: {0}", closeOnClickedOutside );
            Log.Debug( StackTraceUtility.ExtractStackTrace() );
        }

        public void Notify_TreeInitialized()
        {
            SetRects();
        }

        public override void PreOpen()
        {
            base.PreOpen();
            SetRects();

            if (Tree.ActiveTree == null)
            {
                Tree.ActiveTree = Tree.AllTab();
                Tree.ActiveTree.Initialize();
                // if (!Tree.Trees.ContainsKey("Main"))
                // {
                //
                //         Tree.ActiveTree = new Tree(DefDatabase<ResearchTabDef>.GetNamed("Main"));
                //         Tree.ActiveTree.Initialize();
                //     
                //
                // }


                var projects = DefDatabase<ResearchProjectDef>.AllDefsListForReading;

                var groups = projects.GroupBy(def => def.tab);
                // Tree.AllTab();//.Initialize();

                foreach (var @group in groups)
                {
                    if (!Tree.Trees.ContainsKey(@group.Key.defName))
                        new Tree(@group.Key);
                }

            }
                
            // clear node availability caches
            ResearchNode.ClearCaches();

            _dragging             = false;
            closeOnClickedOutside = false;
        }

        private void SetRects()
        {
            // tree view rects, have to deal with UIScale and ZoomLevel manually.
            _baseViewRect = new Rect(
                StandardMargin                                            / Prefs.UIScale,
                ( TopBarHeight*2 + Constants.Margin + StandardMargin )      / Prefs.UIScale,
                ( Screen.width                    - StandardMargin * 2f ) / Prefs.UIScale,
                ( Screen.height - MainButtonDef.ButtonHeight - StandardMargin * 2f - TopBarHeight*2 - Constants.Margin ) /
                Prefs.UIScale );
            _baseViewRect_Inner = _baseViewRect.ContractedBy( Constants.Margin / Prefs.UIScale );

            // windowrect, set to topleft (for some reason vanilla alignment overlaps bottom buttons).
            windowRect.x      = 0f;
            windowRect.y      = 0f;
            windowRect.width  = UI.screenWidth;
            windowRect.height = UI.screenHeight - MainButtonDef.ButtonHeight;
        }

        public override void DoWindowContents( Rect canvas )
        {
            if ( Tree.ActiveTree == null || !Tree.ActiveTree.Initialized )
                return;


            // top bar
            var tabBarRect = new Rect(
                canvas.xMin,
                canvas.yMin,
                canvas.width,
                TopBarHeight/2 );
            DrawTabBar(tabBarRect);
            var topRect = new Rect(
                canvas.xMin,
                tabBarRect.yMax+Margin,
                tabBarRect.width,
                TopBarHeight);
            DrawTopBar( topRect );

            ApplyZoomLevel();

            // draw background
            GUI.DrawTexture( ViewRect, Assets.SlightlyDarkBackground );

            // draw the actual tree
            // TODO: stop scrollbars scaling with zoom
            _scrollPosition = GUI.BeginScrollView( ViewRect, _scrollPosition, Tree.ActiveTree.TreeRect );
            GUI.BeginGroup(
                new Rect(
                    ScaledMargin,
                    ScaledMargin,
                    Tree.ActiveTree.TreeRect.width  + ScaledMargin * 2f,
                    Tree.ActiveTree.TreeRect.height + ScaledMargin * 2f
                )
            );
            if(Tree.ActiveTree.Initialized)
                Tree.ActiveTree.Draw( VisibleRect );
            Queue.DrawLabels( VisibleRect );

            HandleZoom();

            GUI.EndGroup();
            GUI.EndScrollView( false );

            HandleDragging(topRect);
            HandleDolly();

            // reset zoom level
            ResetZoomLevel();


            // cleanup;
            GUI.color   = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawTabBar(Rect canvas)
        {
            Profiler.Start("Tree.DrawTab");
          

            var pos = canvas.min;

            var tabAmount = Tree.Tabs.Count;
            var resolution = UI.screenWidth;
            var size = Mathf.Min(NodeSize.x + Margin, (resolution - 2 * Margin) / tabAmount);
            Tree.ActiveTree.DrawSave(new Rect(
                pos.x,
                pos.y,
                size,
                NodeSize.y / 2 + Margin));
            pos.x += size;

            for (var i = 0; i < Tree.Tabs.Count && pos.x + NodeSize.x < canvas.xMax; i++)
            {
                var node = Tree.Trees[Tree.Tabs[i].defName];
                var rect = new Rect(
                    pos.x ,
                    pos.y ,
                    size,
                    NodeSize.y/2 + Margin
                );

                node.DrawTab(rect);

                pos.x += size;
            }

            Profiler.End();
        }

        private void HandleDolly()
        {
            var dollySpeed = 10f;
            if ( KeyBindingDefOf.MapDolly_Left.IsDown )
                _scrollPosition.x -= dollySpeed;
            if ( KeyBindingDefOf.MapDolly_Right.IsDown )
                _scrollPosition.x += dollySpeed;
            if ( KeyBindingDefOf.MapDolly_Up.IsDown )
                _scrollPosition.y -= dollySpeed;
            if ( KeyBindingDefOf.MapDolly_Down.IsDown )
                _scrollPosition.y += dollySpeed;
        }


        private void HandleZoom()
        {
            // handle zoom
            if ( Event.current.isScrollWheel )
            {
                // absolute position of mouse on research tree
                var absPos = Event.current.mousePosition;
                // Log.Debug( "Absolute position: {0}", absPos );

                // relative normalized position of mouse on visible tree
                var relPos = ( Event.current.mousePosition - _scrollPosition ) / ZoomLevel;
                // Log.Debug( "Normalized position: {0}", relPos );

                // update zoom level
                ZoomLevel += Event.current.delta.y * ZoomStep * ZoomLevel;

                // we want to keep the _normalized_ relative position the same as before zooming
                _scrollPosition = absPos - relPos * ZoomLevel;

                Event.current.Use();
            }
        }


        private void HandleDragging(Rect topRect)
        {
            var inTopRect = topRect.Contains(Event.current.mousePosition);
            if ( Event.current.type == EventType.MouseDown && !inTopRect)
            {
                _dragging      = true;
                _mousePosition = Event.current.mousePosition;
                Event.current.Use();
            }

            //Dragging Queue
            if (Event.current.type == EventType.MouseDown && inTopRect)
            {
                _dragging = true;
                _mousePosition = Event.current.mousePosition;
                Event.current.Use();
            }



            if ( Event.current.type == EventType.MouseUp )
            {
                _dragging      = false;
                _mousePosition = Vector2.zero;
            }

            if ( Event.current.type == EventType.MouseDrag && !inTopRect)
            {
                var _currentMousePosition = Event.current.mousePosition;
                _scrollPosition += _mousePosition - _currentMousePosition;
                _mousePosition  =  _currentMousePosition;
            }
        }

        private void ApplyZoomLevel()
        {
            GUI.EndClip(); // window contents
            GUI.EndClip(); // window itself?
            GUI.matrix = Matrix4x4.TRS( new Vector3( 0f, 0f, 0f ), Quaternion.identity,
                                        new Vector3( Prefs.UIScale / ZoomLevel, Prefs.UIScale / ZoomLevel, 1f ) );
        }

        private void ResetZoomLevel()
        {
            // dummies to maintain correct stack size
            // TODO; figure out how to get actual clipping rects in ApplyZoomLevel();
            UI.ApplyUIScale();
            GUI.BeginClip( windowRect );
            GUI.BeginClip( new Rect( 0f, 0f, UI.screenWidth, UI.screenHeight ) );
        }

        private void DrawTopBar( Rect canvas )
        {
            var searchRect = canvas;
            var queueRect  = canvas;
            searchRect.width =  200f;
            queueRect.xMin   += 200f + Constants.Margin;

            GUI.DrawTexture( searchRect, Assets.SlightlyDarkBackground );
            GUI.DrawTexture( queueRect, Assets.SlightlyDarkBackground );

            DrawSearchBar( searchRect.ContractedBy( Constants.Margin ) );
            Queue.DrawQueue( queueRect.ContractedBy( Constants.Margin ), !_dragging );
        }

        private void DrawSearchBar( Rect canvas )
        {
            Profiler.Start( "DrawSearchBar" );
            var iconRect = new Rect(
                    canvas.xMax - Constants.Margin - 16f,
                    0f,
                    16f,
                    16f )
               .CenteredOnYIn( canvas );
            var searchRect = new Rect(
                    canvas.xMin,
                    0f,
                    canvas.width,
                    30f )
               .CenteredOnYIn( canvas );

            GUI.DrawTexture( iconRect, Assets.Search );
            var query = Widgets.TextField( searchRect, _query );

            if ( query != _query )
            {
                _query = query;
                Find.WindowStack.FloatMenu?.Close( false );

                if ( query.Length > 2 )
                {
                    // open float menu with search results, if any.
                    var options = new List<FloatMenuOption>();

                    foreach ( var result in Tree.ActiveTree.Nodes.OfType<ResearchNode>()
                                                .Select( n => new {node = n, match = n.Matches( query )} )
                                                .Where( result => result.match > 0 )
                                                .OrderBy( result => result.match ) )
                        options.Add( new FloatMenuOption( result.node.Label, () => CenterOn( result.node ),
                                                          MenuOptionPriority.Default, () => CenterOn( result.node ) ) );

                    if ( !options.Any() )
                        options.Add( new FloatMenuOption( "Fluffy.ResearchTree.NoResearchFound".Translate(), null ) );

                    Find.WindowStack.Add( new FloatMenu_Fixed( options,
                                                               UI.GUIToScreenPoint(
                                                                   new Vector2(
                                                                       searchRect.xMin, searchRect.yMax ) ) ) );
                }
            }

            Profiler.End();
        }

        public void CenterOn( Node node )
        {
            var position = new Vector2(
                ( NodeSize.x + NodeMargins.x ) * ( node.X - .5f ),
                ( NodeSize.y + NodeMargins.y ) * ( node.Y - .5f ) );

            node.Highlighted = true;

            position -= new Vector2( UI.screenWidth, UI.screenHeight ) / 2f;

            position.x      = Mathf.Clamp( position.x, 0f, Tree.ActiveTree.TreeRect.width  - ViewRect.width );
            position.y      = Mathf.Clamp( position.y, 0f, Tree.ActiveTree.TreeRect.height - ViewRect.height );
            _scrollPosition = position;
        }
    }
}