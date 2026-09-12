using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Google.Protobuf.Protocol;

namespace Server.Game
{
    public class ObjectManager
    {
        public static ObjectManager Instance { get; } = new ObjectManager();

        object _lock = new object();
        Dictionary<int, Player> _players = new Dictionary<int, Player>();
        Dictionary<int, Monster> _monsters = new Dictionary<int, Monster>();
        Dictionary<int, int> _teams = new Dictionary<int, int>(); // key: objectId, value: team

        // [UNUSED(1)][TYPE(7)][ID(24)]
        int _counter = 0;

        public T Add<T>() where T : GameObject, new()
        {
            T gameObject = new T();

            lock (_lock) 
            {
                gameObject.Id = GenerateId(gameObject.ObjectType);

                if (gameObject.ObjectType == GameObjectType.Player)
                {
                    _players.Add(gameObject.Id, gameObject as Player);
                }
                else if (gameObject.ObjectType == GameObjectType.Monster)
                {
                    _monsters.Add(gameObject.Id, gameObject as Monster);
                }
            }

            return gameObject;
        }

        int GenerateId(GameObjectType type)
        {
            lock (_lock)
            {
                return ((int)type << 24) | (_counter++);
            }
        }

        public static GameObjectType GetObjectTypeById(int id)
        {
            int type = (id >> 24) & 0x7f;
            return (GameObjectType)type;
        }

        public bool Remove(int objectId)
        {
            GameObjectType objectType = GetObjectTypeById(objectId);

            lock (_lock)
            {
                if(objectType == GameObjectType.Player)
                {
                    bool removed = _players.Remove(objectId);
                    _teams.Remove(objectId);
                    return removed;
                }
                else if (objectType == GameObjectType.Monster)
                {
                    bool removed = _monsters.Remove(objectId);
                    _teams.Remove(objectId);
                    return removed;
                }
            }

            return false;
        }

        public GameObject Find(int objectId)
        {
            GameObjectType objectType = GetObjectTypeById(objectId);

            lock (_lock)
            {
                if (objectType == GameObjectType.Player)
                {
                    Player player = null;
                    if (_players.TryGetValue(objectId, out player))
                        return player;
                }
                else if (objectType == GameObjectType.Monster)
                {
                    Monster monster = null;
                    if (_monsters.TryGetValue(objectId, out monster))
                        return monster;
                }
            }
            return null;
        }

        public void RegisterTeam(int ObjectId, int team)
        {
            lock (_lock)
            {
                _teams.Add(ObjectId, team);
            }
        }

        public int GetTeam(int objectId)
        {
            lock (_lock)
            {
                return _teams.TryGetValue(objectId, out int team) ? team : -1;
            }
        }
      
        public int GetPlayerCount()
        {
            lock (_lock)
            {
                return _players.Count;
            }
        }
    }
}
