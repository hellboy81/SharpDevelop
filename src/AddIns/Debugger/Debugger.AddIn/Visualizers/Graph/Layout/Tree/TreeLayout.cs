﻿// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the BSD license (for details please see \src\AddIns\Debugger\Debugger.AddIn\license.txt)

using System;
using System.Collections.Generic;
using System.Windows;
using Debugger.AddIn.Visualizers.Graph.Drawing;
using System.Linq;

namespace Debugger.AddIn.Visualizers.Graph.Layout
{
	/// <summary>
	/// Calculates layout of <see cref="ObjectGraph" />, producing <see cref="PositionedGraph" />.
	/// </summary>
	public class TreeLayout
	{
		private static readonly double NodeMarginH = 30;
		private static readonly double NodeMarginV = 30;
		
		GraphEdgeRouter edgeRouter = new GraphEdgeRouter();
		/// <summary>
		/// The produced layout is either a horizontal or vertical tree.
		/// </summary>
		public LayoutDirection LayoutDirection { get; private set; }
		
		public TreeLayout(LayoutDirection layoutDirection)
		{
			this.LayoutDirection = layoutDirection;
		}
		
		/// <summary>
		/// Calculates layout for given <see cref="ObjectGraph" />.
		/// </summary>
		/// <param name="objectGraph"></param>
		/// <returns></returns>
		public PositionedGraph CalculateLayout(ObjectGraph objectGraph, Expanded expanded)
		{
			var positionedGraph = BuildPositionedGraph(objectGraph, expanded);
			CalculateLayout(positionedGraph);
			this.edgeRouter.RouteEdges(positionedGraph);
			
			return positionedGraph;
		}
		
		// Expanded is passed so that the correct ContentNodes are expanded in the PositionedNode
		PositionedGraph BuildPositionedGraph(ObjectGraph objectGraph, Expanded expanded)
		{
			var positionedNodeFor = new Dictionary<ObjectGraphNode, PositionedNode>();
			var positionedGraph = new PositionedGraph();
			
			// create empty PositionedNodes
			foreach (ObjectGraphNode objectNode in objectGraph.ReachableNodes) {
				var posNode = new PositionedNode(objectNode);
				posNode.InitContentFromObjectNode(expanded);
				posNode.MeasureVisualControl();
				positionedGraph.AddNode(posNode);
				positionedNodeFor[objectNode] = posNode;
			}
			
			// create edges
			foreach (PositionedNode posNode in positionedGraph.Nodes)
			{
				foreach (PositionedNodeProperty property in posNode.Properties)	{
					if (property.ObjectGraphProperty.TargetNode != null) {
						ObjectGraphNode targetObjectNode = property.ObjectGraphProperty.TargetNode;
						PositionedNode edgeTarget = positionedNodeFor[targetObjectNode];
						property.Edge = new PositionedEdge {
							Name = property.Name, Source = property, Target = edgeTarget
						};
					}
				}
			}
			positionedGraph.Root = positionedNodeFor[objectGraph.Root];
			return positionedGraph;
		}

		void CalculateLayout(PositionedGraph positionedGraph)
		{
			HashSet<PositionedNode> seenNodes = new HashSet<PositionedNode>();
			HashSet<PositionedEdge> treeEdges = new HashSet<PositionedEdge>();
			// first layout pass
			CalculateSubtreeSizes(positionedGraph.Root, seenNodes, treeEdges);
			// second layout pass
			CalculateNodePosRecursive(positionedGraph.Root, treeEdges, 0, 0);
		}
		
		// determines which edges are tree edges, and calculates subtree size for each node
		private void CalculateSubtreeSizes(PositionedNode root, HashSet<PositionedNode> seenNodes, HashSet<PositionedEdge> treeEdges)
		{
			seenNodes.Add(root);
			double subtreeSize = 0;
			foreach (var property in root.Properties) {
				var edge = property.Edge;
				if (edge != null) {
					var targetNode = edge.Target;
					if (!seenNodes.Contains(targetNode)) {
						// when we come to a node for the first time, we declare the incoming edge a tree edge
						treeEdges.Add(edge);
						CalculateSubtreeSizes(targetNode, seenNodes, treeEdges);
						subtreeSize += targetNode.SubtreeSize;
					}
				}
			}
			root.SubtreeSize = Math.Max(GetLateralSizeWithMargin(root), subtreeSize);
		}
		
		
		/// <summary>
		/// Given SubtreeSize for each node, positions the nodes, in a left-to-right or top-to-bottom layout.
		/// </summary>
		/// <param name="node"></param>
		/// <param name="lateralStart"></param>
		/// <param name="mainStart"></param>
		void CalculateNodePosRecursive(PositionedNode node, HashSet<PositionedEdge> treeEdges, double lateralBase, double mainBase)
		{
			double childsSubtreeSize = TreeChildNodes(node, treeEdges).Sum(child => child.SubtreeSize);
			double center = TreeEdges(node, treeEdges).Count() == 0 ? 0 : 0.5 * (childsSubtreeSize - (GetLateralSizeWithMargin(node)));
			if (center < 0)	{
				// if root is larger than subtree, it would be shifted below lateralStart
				// -> make whole layout start at lateralStart
				lateralBase -= center;
			}
			
			SetLateral(node, GetLateral(node) + lateralBase + center);
			SetMain(node, mainBase);
			
			double childLateral = lateralBase;
			double childsMainFixed = GetMain(node) + GetMainSizeWithMargin(node);
			foreach (var child in TreeChildNodes(node, treeEdges)) {
				CalculateNodePosRecursive(child, treeEdges, childLateral, childsMainFixed);
				childLateral += child.SubtreeSize;
			}
		}
		
		IEnumerable<PositionedEdge> TreeEdges(PositionedNode node, HashSet<PositionedEdge> treeEdges)
		{
			return node.Edges.Where(e => treeEdges.Contains(e));
		}
		
		IEnumerable<PositionedNode> TreeChildNodes(PositionedNode node, HashSet<PositionedEdge> treeEdges)
		{
			return TreeEdges(node, treeEdges).Select(e => e.Target);
		}
		
		#region Horizontal / vertical layout helpers
		
		double GetMainSizeWithMargin(PositionedNode node)
		{
			return (this.LayoutDirection == LayoutDirection.LeftRight) ? node.Width + NodeMarginH : node.Height + NodeMarginV;
		}
		
		double GetLateralSizeWithMargin(PositionedNode node)
		{
			return (this.LayoutDirection == LayoutDirection.LeftRight) ? node.Height + NodeMarginV : node.Width + NodeMarginH;
		}
		
		double GetMain(PositionedNode node)
		{
			return (this.LayoutDirection == LayoutDirection.LeftRight) ? node.Left : node.Top;
		}
		
		double GetLateral(PositionedNode node)
		{
			return (this.LayoutDirection == LayoutDirection.LeftRight) ? node.Top : node.Left;
		}
		
		void SetMain(PositionedNode node, double value)
		{
			if (this.LayoutDirection == LayoutDirection.LeftRight) {
				node.Left = value;
			} else {
				node.Top = value;
			}
		}
		
		void SetLateral(PositionedNode node, double value)
		{
			if (this.LayoutDirection == LayoutDirection.LeftRight) {
				node.Top = value;
			} else {
				node.Left = value;
			}
		}
		
		#endregion
	}
}
