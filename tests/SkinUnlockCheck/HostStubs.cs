using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object value) => value != null; }
    public class ScriptableObject : Object { public static T CreateInstance<T>() where T : new() => new T(); }
    public class GameObject : Object { public object Component; public T GetComponent<T>() => (T)Component; }
}
namespace RoR2
{
    public class CharacterBody : UnityEngine.Object { public string baseNameToken; }
    public class CharacterMaster : UnityEngine.Object { public UnityEngine.GameObject bodyPrefab; }
    public class NetworkUser : UnityEngine.Object { public NetId netId; }
    public struct NetId { public uint Value; }
    public class LocalUser { public CharacterBody cachedBody; public CharacterMaster cachedMaster; public UserProfile userProfile; public NetworkUser currentNetworkUser; }
    public class UserProfile
    {
        readonly HashSet<UnlockableDef> owned = new HashSet<UnlockableDef>();
        public int Saves;
        public bool HasUnlockable(UnlockableDef def) => owned.Contains(def);
        public void GrantUnlockable(UnlockableDef def) => owned.Add(def);
        public void RequestEventualSave() => Saves++;
    }
    public static class LocalUserManager { public static List<LocalUser> readOnlyLocalUsersList = new List<LocalUser>(); }
    public class UnlockableDef : UnityEngine.Object { public string cachedName, nameToken; public Func<string> getHowToUnlockString, getUnlockedString; }
    public class Run : UnityEngine.Object { public static Run instance; public int stageClearCount; }
    public class TeleporterInteraction { public static event Action<TeleporterInteraction> onTeleporterChargedGlobal; public static void Charge() => onTeleporterChargedGlobal?.Invoke(new TeleporterInteraction()); }
    public static class Chat { public static void AddMessage(string message) {} }
}
namespace RoR2.ContentManagement
{
    public class ContentPack { public Collection<RoR2.UnlockableDef> unlockableDefs = new Collection<RoR2.UnlockableDef>(); }
    public class Collection<T> : List<T> { public void Add(T[] values) => AddRange(values); }
}
namespace OlimarTest
{
    public static class OlimarSkin { public const uint Olimar=0, Louie=1, Pianta=2, Pikachu=3; }
    public static class Plugin { public static Logger Log = new Logger(); }
    public class Logger { public void LogInfo(string message) {} }
}
