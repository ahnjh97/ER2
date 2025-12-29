using Nito.Collections;
using System;
using System.Collections.Generic;
using System.Numerics;
using static Server.Game.Node;

namespace Server.Game
{
    struct Funnel
    {
        public void Clear()
        {
        }

        // 경로 후처리 : 퍼널 알고리즘
        public void GetFunnelPath(List<Gate> channel, Vector3 start, Vector3 end, out Deque<Node> output)
        {
            output = new Deque<Node>();
     
            if (channel.Count == 0)
            {
                // 시작점과 목표점만 경로에 추가
                Node startPosNode = new Node(start, Node.NodeType.Point);
                Node endPosNode = new Node(end, Node.NodeType.Point);

                output.AddToBack(startPosNode);
                output.AddToBack(endPosNode);

                return;
            }

            Vector3 apex = start;

            Vector3 portalLeft = start;
            Vector3 portalRight = start;

            int leftIndex = 0;
            int rightIndex = 0;

            output.AddToBack(new Node(start, Node.NodeType.Point));

            // ***************************************************************
            //  Funnel 알고리즘 
            // ***************************************************************

            for (int i = 0; i < channel.Count; i++)
            {
                Gate currentGate = channel[i];

                Vector3 currentLeft = currentGate.Left;
                Vector3 currentRight = currentGate.Right;

                // 1. 새로운 정점이 왼쪽 경계를 좁히는 경우
                if (Cross(new Vector2(portalLeft.X - apex.X, portalLeft.Z - apex.Z),
                          new Vector2(currentLeft.X - apex.X, currentLeft.Z - apex.Z)) <= 0)
                {
                    if (Cross(new Vector2(currentLeft.X - apex.X, currentLeft.Z - apex.Z), // 경로가 꼬였는 지 확인
                              new Vector2(portalRight.X - apex.X, portalRight.Z - apex.Z)) > 0)
                    {
                        output.AddToBack(new Node(portalRight, Node.NodeType.Point)); 
                        apex = portalRight;
                        portalLeft = apex;
                        portalRight = apex;
                        i = rightIndex; 
                        continue;
                    }

                    portalLeft = currentLeft;
                    leftIndex = i;
                }

                // 2. 새로운 정점이 오른쪽 경계를 좁히는 경우
                if (Cross(new Vector2(portalRight.X - apex.X, portalRight.Z - apex.Z),
                          new Vector2(currentRight.X - apex.X, currentRight.Z - apex.Z)) >= 0)
                {
                    if (Cross(new Vector2(currentRight.X - apex.X, currentRight.Z - apex.Z),
                              new Vector2(portalLeft.X - apex.X, portalLeft.Z - apex.Z)) < 0)
                    {
                        output.AddToBack(new Node(portalLeft, Node.NodeType.Point)); 
                        apex = portalLeft;
                        portalLeft = apex;
                        portalRight = apex;
                        i = leftIndex; 
                        continue;
                    }

                    portalRight = currentRight;
                    rightIndex = i;
                }
            }

            // ***************************************************************
            //  최종 목표
            // ***************************************************************

            output.AddToBack(new Node(end, Node.NodeType.Point));
        }
        #region Helper Functions
        public float Cross(Vector2 a, Vector2 b)
        {
            return a.X * b.Y - a.Y * b.X;
        }

        #endregion
    }


}
