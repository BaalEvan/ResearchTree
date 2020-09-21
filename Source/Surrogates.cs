using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ResearchTree
{
    using System.Runtime.Serialization;
    using UnityEngine;
    using Verse;

    public struct IntVec2Surrogate : ISerializationSurrogate
    {
        public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
        {
            IntVec2 intVec2 = (IntVec2) obj;
            info.AddValue("x",intVec2.x);
            info.AddValue("z",intVec2.z);
        }

        public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector selector)
        {
            IntVec2 intVec2 = (IntVec2) obj;
            intVec2.x = info.GetInt32("x");
            intVec2.z = info.GetInt32("z");
            return intVec2;
        }
    }

    public struct ResearchProjectDefSurrogate : ISerializationSurrogate
    {
        public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
        {
            ResearchProjectDef researchProjectDef = (ResearchProjectDef) obj;
            info.AddValue("defName", researchProjectDef.defName);

        }

        public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector selector)
        {
            ResearchProjectDef researchProjectDef = DefDatabase<ResearchProjectDef>.AllDefsListForReading.Find(def => def.defName==info.GetString("defName"));
            return researchProjectDef;
        }
    }

    public struct Vector2Surrogate : ISerializationSurrogate
    {
        public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
        {
            Vector2 vector2 = (Vector2) obj;
            info.AddValue("x", vector2.x);
            info.AddValue("y", vector2.y);
        }

        public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector selector)
        {
            Vector2 vector2 = (Vector2) obj;
            vector2.x = info.GetInt32("x");
            vector2.y = info.GetInt32("y");
            return vector2;
        }
    }
    public struct IntRangeSurrogate : ISerializationSurrogate
    {
        public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
        {
            IntRange intRange = (IntRange) obj;
            info.AddValue("min", intRange.min);
            info.AddValue("max", intRange.max);
        }

        public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector selector)
        {
            IntRange intRange = (IntRange) obj;
            intRange.min = info.GetInt32("min");
            intRange.max = info.GetInt32("max");
            return intRange;
        }
    }

    public struct RectSurrogate : ISerializationSurrogate
    {
        public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
        {
            Rect rect = (Rect) obj;
            info.AddValue("x", rect.x);
            info.AddValue("y", rect.y);
            info.AddValue("width", rect.width);
            info.AddValue("height", rect.height);

        }

        public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector selector)
        {
            Rect rect = (Rect) obj;
            rect.x = info.GetInt32("x");
            rect.y = info.GetInt32("y");
            rect.width = info.GetInt32("width");
            rect.height = info.GetInt32("height");
            return rect;
        }
    }
}
