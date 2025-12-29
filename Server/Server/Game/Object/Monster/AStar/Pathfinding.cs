using System;
using System.Collections.Generic;
using System.Numerics;
using ServerCore;
using System.Linq;

namespace Server.Game
{
    readonly unsafe struct Pathfinding
    {
        private static NavMeshExportData _navMeshData;
        private static List<Node> _triangleNodes;

        readonly ServerCore.PriorityQueue<Node, float> _open;
        readonly Dictionary<int, float> _gScore;

        readonly private Dictionary<(int, int), List<Node>> _spatialGrid;
        private const float GRID_SIZE = 10f;

        public Pathfinding(List<Node> triangle, NavMeshExportData nav) : this()
        {
            _spatialGrid = new Dictionary<(int, int), List<Node>>();
            _triangleNodes = triangle;
            _navMeshData = nav;
            _open = new ServerCore.PriorityQueue<Node, float>();
            _gScore = new Dictionary<int, float>();

            BuildSpatialGrid();
        }

        public void Clear()
        {
            while (_open.Count > 0)
                _open.Pop();
            _gScore.Clear();
        }
        private void BuildSpatialGrid()
        {
            foreach (var node in _triangleNodes)
            {
                int gridX = (int)(node.Center.X / GRID_SIZE);
                int gridZ = (int)(node.Center.Z / GRID_SIZE);
                var key = (gridX, gridZ);

                if (!_spatialGrid.ContainsKey(key))
                    _spatialGrid[key] = new List<Node>();

                _spatialGrid[key].Add(node);
            }
        }
        public PathState PathSearch(Vector3 start, Vector3 end, out List<Gate> channel)
        {
            channel = new List<Gate>();

            _gScore.Clear();

            Node startNode = FindNearestNode(start);
            Node targetNode = FindNearestNode(end);

            if (startNode == null || targetNode == null)
                return PathState.NoPath;

            HashSet<Node> visited = new HashSet<Node>();

            _gScore[startNode.Index] = 0;
            float startF = Vector3.Distance(startNode.Center, targetNode.Center);
            _open.Push(startNode, startF);

            while (_open.Count > 0)
            {
                Node currentNode = _open.Pop();

                //  방문 체크
                if (!visited.Add(currentNode))
                    continue;

                currentNode.Parent = null; 

                if (currentNode.Equals(targetNode))
                {
                    channel = RetracePath(startNode, currentNode);

                    foreach (var node in visited)
                        node.Parent = null;

                    return PathState.PathFound;
                }

                if (!_gScore.TryGetValue(currentNode.Index, out float currentGScore))
                    continue;

                foreach (var neighbor in currentNode.Neighbors)
                {
                    float tentativeGScore = currentGScore + Vector3.Distance(currentNode.Center, neighbor.Center); // 현재까지 온 거리+ 현재 노드에서 이웃 노드까지의 직선 거리

                    if (_gScore.TryGetValue(neighbor.Index, out float neighborG) && tentativeGScore >= neighborG) // 더 빠른 길이 있었는가?
                        continue;

                    neighbor.Parent = currentNode;
                    _gScore[neighbor.Index] = tentativeGScore;

                    float hScore = Vector3.Distance(neighbor.Center, targetNode.Center);
                    float newFScore = tentativeGScore + hScore; // 현재 노드까지 + 휴리스틱 
                    _open.Push(neighbor, newFScore);
                }
            }

            foreach (var node in visited)
                node.Parent = null;

            return PathState.NoPath;
        }

        const float MAX_SEARCH_DIST_SQ = 20f * 20f;
        public Node FindNearestNode(Vector3 position)
        {
            int gridX = (int)(position.X / GRID_SIZE);
            int gridZ = (int)(position.Z / GRID_SIZE);

            float minDistance = float.MaxValue;
            Node nearestNode = null;

            // 주변 9개 그리드만 검색
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    var key = (gridX + dx, gridZ + dz);

                    if (!_spatialGrid.TryGetValue(key, out List<Node> nodes))
                        continue;

                    foreach (var node in nodes)
                    {
                        float dx2 = node.Center.X - position.X;
                        float dy2 = node.Center.Y - position.Y;
                        float dz2 = node.Center.Z - position.Z;
                        float distanceSq = dx2 * dx2 + dy2 * dy2 + dz2 * dz2;

                        if (distanceSq < minDistance * minDistance)
                        {
                            minDistance = (float)Math.Sqrt(distanceSq);
                            nearestNode = node;
                        }
                    }
                }
            }
            if (minDistance * minDistance > MAX_SEARCH_DIST_SQ)
                return null;

            return nearestNode;
        }

        // 역추적
        List<Gate> RetracePath(Node startNode, Node endNode)
        {
            List<Node> nodePath = new List<Node>();
            Node currentNode = endNode;

            while (currentNode != null)
            {
                nodePath.Add(currentNode);
                currentNode = currentNode.Parent;
            }
            nodePath.Reverse();

            List<Gate> gateChannel = new List<Gate>();

            for (int i = 0; i < nodePath.Count - 1; i++)
            {
                Node nodeA = nodePath[i];
                Node nodeB = nodePath[i + 1];

                Gate gate;
                if (GetSharedEdge(nodeA, nodeB, out gate) == PathState.PathFound)
                    gateChannel.Add(gate);
            }
            return gateChannel;
        }

        // 두 삼각형 간의 공유된 변 찾기.
        private PathState GetSharedEdge(Node nodeA, Node nodeB, out Gate gate)
        {
            gate = new Gate();

            int triAStart = nodeA.Index * 3;
            int triBStart = nodeB.Index * 3;

            int common1 = -1;
            int common2 = -1;
            int foundCount = 0;

            for (int i = 0; i < 3 && foundCount < 2; i++)
            {
                int vA = _navMeshData.triangles[triAStart + i];
                for (int j = 0; j < 3; j++)
                {
                    int vB = _navMeshData.triangles[triBStart + j];

                    if (vA == vB)
                    {
                        if (foundCount == 0)
                            common1 = vA;
                        else if (foundCount == 1)
                            common2 = vA;

                        foundCount++;
                        break;
                    }
                }
            }

            if (foundCount == 2)
            {
                Vector3 v1 = _navMeshData.vertices[common1];
                Vector3 v2 = _navMeshData.vertices[common2];

                gate.Left = v1;
                gate.Right = v2;
                return PathState.PathFound;
            }
            return PathState.EdgeInvalid;
        }
    }
}