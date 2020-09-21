using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ResearchTree
{
    using System.IO;
    using System.Runtime.Serialization;
    using System.Runtime.Serialization.Formatters.Binary;
    using System.Security.Cryptography;
    using FluffyResearchTree;
    using UnityEngine;
    using Verse;
    using Log = Verse.Log;

    public static class CacheIO
    {
        public static bool CacheShouldBeLoaded = false;
        private static bool _hashVerified;

        public static bool VerifyHash()
        {
            return AnyModChanges(LoadHash());
        }

        private static bool AnyModChanges(string oldHash)
        {
            if (!_hashVerified)
            {
                CacheShouldBeLoaded = oldHash.Equals(GetHash());
                _hashVerified = true;
            }
            
            return !CacheShouldBeLoaded;
        }

        private static string GetHash()
        {
            var modlist = string.Concat(ModsConfig.ActiveModsInLoadOrder.Select(m => m.PackageId + m.Active + m.Description));

            using (SHA1 sha1Hash = SHA1.Create())
            {
                //From String to byte array
                byte[] sourceBytes = Encoding.UTF8.GetBytes(modlist);
                byte[] hashBytes = sha1Hash.ComputeHash(sourceBytes);
                string hash = BitConverter.ToString(hashBytes).Replace("-", String.Empty);
                return hash;
            }
        }

        private static string LoadHash()
        {
            if (!File.Exists(GetCachePatchForTab("hash")))
            {
                return string.Empty;
            }

            FileStream load = new FileStream(GetCachePatchForTab("hash"), FileMode.Open);
            StreamReader sr = new StreamReader(load);
            var hash = sr.ReadLine();
            load.Close();
            return hash;

        }

        private static void SaveHash()
        {
            FileStream load = new FileStream(GetCachePatchForTab("hash"), FileMode.Truncate);
            StreamWriter sw = new StreamWriter(load);
            sw.WriteLine(GetHash());
            sw.Close();
        }

        private static string GetCachePatchForTab(string tabName)
        {
            var path = Path.Combine(GenFilePaths.SaveDataFolderPath, "ResearchTabConfig");
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            return Path.Combine(path, tabName + ".dat");
        }


        public static void SaveTab(Tree tabTree)
        {
            SaveHash();

            FileStream fs = new FileStream(GetCachePatchForTab(tabTree.TabName), FileMode.CreateNew);
            BinaryFormatter bf = new BinaryFormatter();
            var surrogates = new SurrogateSelector();
            surrogates.AddSurrogate(typeof(IntVec2), new StreamingContext(StreamingContextStates.All), new IntVec2Surrogate());
            surrogates.AddSurrogate(typeof(Rect), new StreamingContext(StreamingContextStates.All), new RectSurrogate());
            surrogates.AddSurrogate(typeof(IntRange), new StreamingContext(StreamingContextStates.All), new IntRangeSurrogate());
            surrogates.AddSurrogate(typeof(Vector2), new StreamingContext(StreamingContextStates.All), new Vector2Surrogate());
            surrogates.AddSurrogate(typeof(ResearchProjectDef), new StreamingContext(StreamingContextStates.All), new ResearchProjectDefSurrogate());
            bf.SurrogateSelector = surrogates;
            try
            {
                bf.Serialize(fs, tabTree);
            }
            catch (SerializationException e)
            {
                Log.Error("Failed to serialize. Reason: " + e.Message, true);
            }
            finally
            {
                fs.Close();
            }
        }

        public static Tree LoadFromCache(string tabName)
        {
            if (!File.Exists(GetCachePatchForTab(tabName)))
            {
                return null;
            }

            BinaryFormatter bf = new BinaryFormatter();
            var surrogates = new SurrogateSelector();
            surrogates.AddSurrogate(typeof(IntVec2), new StreamingContext(StreamingContextStates.All), new IntVec2Surrogate());
            surrogates.AddSurrogate(typeof(Rect), new StreamingContext(StreamingContextStates.All), new RectSurrogate());
            surrogates.AddSurrogate(typeof(IntRange), new StreamingContext(StreamingContextStates.All), new IntRangeSurrogate());
            surrogates.AddSurrogate(typeof(ResearchProjectDef), new StreamingContext(StreamingContextStates.All), new ResearchProjectDefSurrogate());
            surrogates.AddSurrogate(typeof(Vector2), new StreamingContext(StreamingContextStates.All), new Vector2Surrogate());

            bf.SurrogateSelector = surrogates;

            FileStream load = new FileStream(GetCachePatchForTab(tabName), FileMode.Open);

            Tree loadedTree = new Tree();
            try
            {
                loadedTree = (Tree) bf.Deserialize(load);
            }
            catch (SerializationException e)
            {
                FluffyResearchTree.Log.Debug("Failed to Deserialize. Reason: " + e.Message);
                load.Close();

                return null;
            }
            finally
            {
                if (loadedTree == null || string.IsNullOrEmpty(loadedTree.TabName) || string.IsNullOrEmpty(loadedTree.TabLabel))
                {
                    FluffyResearchTree.Log.Debug($"LoadedTree ({tabName}) is Null, TabName or TabLabel is Empty - FAILED");
                }

                load.Close();
                
            }

            return loadedTree;

        }

    }
}
