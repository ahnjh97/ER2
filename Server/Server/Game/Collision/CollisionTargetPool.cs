using System.Collections.Generic;

namespace Server.Game
{
    // 대여한 리스트는 반환 전까지 한 곳에서만 사용한다.
    // 초기 수량과 용량은 히트박스나 대상 수의 상한이 아니며, 부족하면 확장한다.
    internal static class CollisionTargetPool<T>
    {
        private const int InitialListCount = 32;
        private const int InitialTargetCapacity = 32;
        private static readonly Stack<List<T>> Free = CreatePool();

        private static Stack<List<T>> CreatePool()
        {
            var lists = new Stack<List<T>>(InitialListCount);
            for (int i = 0; i < InitialListCount; i++)
                lists.Push(new List<T>(InitialTargetCapacity));
            return lists;
        }

        internal static List<T> Rent()
        {
            lock (Free)
                return Free.Count > 0 ? Free.Pop() : new List<T>(InitialTargetCapacity);
        }

        internal static void Return(List<T> targets)
        {
            targets.Clear(); // 대상 참조를 제거하고 내부 배열은 재사용한다.
            lock (Free)
                Free.Push(targets);
        }
    }

    internal static class PooledCollisionCallback
    {
        internal static void Queue<T>(GameRoom room, Player player, List<T> source,
            GameObject.StatusEffect effect) where T : GameObject, new()
        {
            var snapshot = CollisionTargetPool<T>.Rent();
            bool queued = false;
            try
            {
                snapshot.AddRange(source);
                room.Push(Execute<T>, room, player, snapshot, effect);
                queued = true;
            }
            finally
            {
                if (!queued)
                    CollisionTargetPool<T>.Return(snapshot);
            }
        }

        private static void Execute<T>(GameRoom room, Player player, List<T> snapshot,
            GameObject.StatusEffect effect) where T : GameObject, new()
        {
            try
            {
                // 실행 시점의 스킬 상태로 콜백을 처리한다.
                // 처리 후 반환할 리스트이므로 핸들러에서 보관하거나 나중에 사용하면 안 된다.
                room.CallOnCollision(player, snapshot, effect);
            }
            finally
            {
                CollisionTargetPool<T>.Return(snapshot);
            }
        }
    }
}
