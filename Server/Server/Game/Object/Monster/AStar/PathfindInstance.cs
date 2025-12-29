using Nito.Collections;
using Server.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace Server.Game
{
    // 폴리곤 정보
    public class Node : IComparable<Node>
    {
        public int Index;
        public Vector3 Center;
        public List<Node> Neighbors;
        public Node Parent;

        //public Node Parent;
        public readonly NodeType Type;
        public Node(Vector3 position, NodeType type)
        {
            Index = -1;
            Center = position;
            Neighbors = new List<Node>();
            Parent = null;
            Type = type;
        }
        public Node(int triangleIndex, Vector3 center) 
        {
            Index = triangleIndex;
            Center = center;
            Neighbors = new List<Node>();
        }

        public int CompareTo(Node other) { return 0;  }
    
        public override bool Equals(object obj)
        {
            return obj is Node node && Index == node.Index;
        }

        public override int GetHashCode()
        {
            return Index.GetHashCode();
        }
        public enum NodeType
        {
            Point,
            Left,
            Right
        }
    }
    // 경로
    struct Gate
    {
        public Vector3 Left;
        public Vector3 Right;
    }

    [Flags]
    public enum PathState
    {
        Inactive = 1,
        EdgeInvalid = 2,
        StartInvalid = 4,
        GoalInvalid = 8,
        NoPath = 16,
        PathFound = 32,
        Invalidated = 64
    }

    public struct PathfindInstance
    {
        private static NavMeshExportData _navMeshData;
        private List<Node> _triangleNodes;

        List<Gate> _channel;
         Deque<Node> _path;
        readonly Funnel _funnel;
        readonly Pathfinding _astar;

        #region Load Navi
        public void Initialize()
        {
            string basePath = ConfigManager.Config.dataPaths["player"];
            string navMeshFilePath = Path.Combine(basePath, "navmesh_data.json");
            string navMeshJsonText = File.ReadAllText(navMeshFilePath);

            _navMeshData = NavMeshExportData.LoadFromJson(navMeshFilePath);
            if (_navMeshData == null)
                return;

            BuildTriangleGraph();
        }

        private void BuildTriangleGraph()
        {
            // 1. 각 삼각형에 대한 Node 객체 생성 및 중심점 계산
            List<int> triangles = _navMeshData.triangles;
            for (int i = 0; i < triangles.Count / 3; i++)
            {
                Vector3 v0Data = _navMeshData.vertices[triangles[i * 3]];
                Vector3 v0 = new Vector3(v0Data.X, v0Data.Y, v0Data.Z);

                Vector3 v1Data = _navMeshData.vertices[triangles[i * 3 + 1]];
                Vector3 v1 = new Vector3(v1Data.X, v1Data.Y, v1Data.Z);

                Vector3 v2Data = _navMeshData.vertices[triangles[i * 3 + 2]];
                Vector3 v2 = new Vector3(v2Data.X, v2Data.Y, v2Data.Z);

                Vector3 center = (v0 + v1 + v2) / 3.0f;
                _triangleNodes.Add(new Node(i, center));
            }

            // 2. 인접한 삼각형 찾기 
            BuildNeighborGraph();

            // 3. 만약 아무것도 연결되지 않은 폴리곤 존재하면 호출될 것임
            foreach (var node in _triangleNodes)
            {
                if (node.Neighbors.Count == 0)
                    Console.WriteLine($"Failed : 이것은 연결되지 않은 폴리곤 {node.Index} .");
            }
        }
        private void BuildNeighborGraph()
        {
            var edgeToNodeMap = new Dictionary<Tuple<Vector3, Vector3>, Node>();

            for (int i = 0; i < _triangleNodes.Count; i++)
            {
                Node currentNode = _triangleNodes[i];
                int[] triIndices = new int[] { _navMeshData.triangles[i * 3], _navMeshData.triangles[i * 3 + 1], _navMeshData.triangles[i * 3 + 2] };

                Vector3[] triVertices = new Vector3[3];
                for (int k = 0; k < 3; k++)
                {
                    Vector3 sv = _navMeshData.vertices[triIndices[k]];
                    triVertices[k] = new Vector3(sv.X, sv.Y, sv.Z);
                }

                for (int j = 0; j < 3; j++)
                {
                    Vector3 v1 = triVertices[j];
                    Vector3 v2 = triVertices[(j + 1) % 3];

                    Vector3 roundedV1 = new Vector3((float)Math.Round(v1.X, 4), (float)Math.Round(v1.Y, 4), (float)Math.Round(v1.Z, 4));
                    Vector3 roundedV2 = new Vector3((float)Math.Round(v2.X, 4), (float)Math.Round(v2.Y, 4), (float)Math.Round(v2.Z, 4));

                    Tuple<Vector3, Vector3> edgeKey;
                    if (IsV1Smaller(roundedV1, roundedV2))
                        edgeKey = new Tuple<Vector3, Vector3>(roundedV1, roundedV2);
                    else
                        edgeKey = new Tuple<Vector3, Vector3>(roundedV2, roundedV1);

                    if (edgeToNodeMap.ContainsKey(edgeKey))
                    {
                        Node neighborNode = edgeToNodeMap[edgeKey];
                        currentNode.Neighbors.Add(neighborNode);
                        neighborNode.Neighbors.Add(currentNode);
                        edgeToNodeMap.Remove(edgeKey);
                    }
                    else
                    {
                        edgeToNodeMap.Add(edgeKey, currentNode);
                    }
                }
            }
        }
        private static bool IsV1Smaller(Vector3 v1, Vector3 v2)
        {
            // 1. X축이 다르면 X축으로 비교
            if (v1.X != v2.X)
                return v1.X < v2.X;

            // 2. X축이 같으면 Y축으로 비교
            if (v1.Y != v2.Y)
                return v1.Y < v2.Y;

            // 3. X, Y축이 같으면 Z축으로 비교
            return v1.Z < v2.Z;
        }
        #endregion
        public PathfindInstance(int idx) : this()
        {
            _path = new Deque<Node>();
            _channel = new List<Gate>();

            _triangleNodes = new List<Node>();
            Initialize();
            _astar = new Pathfinding(_triangleNodes, _navMeshData);
        }
        public PathState FindPath(Vector3 start, Vector3 end, ref Deque<Node> resultPath)
        {
            Clear();

            var result = _astar.PathSearch(start, end, out _channel);
            if (result == PathState.PathFound)
            {
                _funnel.GetFunnelPath(_channel, start, end, out _path);
                resultPath = _path;
            }

            return result;
        }
        public void Clear()
        {
            _astar.Clear();
            _channel.Clear();
            _funnel.Clear();
            _path.Clear();
        }
        private Vector3 RoundPosition(Vector3 pos)
        {
            return new Vector3(
                (float)Math.Round(pos.X),
                (float)Math.Round(pos.Y),
                (float)Math.Round(pos.Z)
            );
        }
    }
}
