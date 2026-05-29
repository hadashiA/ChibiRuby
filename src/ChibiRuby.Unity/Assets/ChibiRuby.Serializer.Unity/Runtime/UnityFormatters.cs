using Unity.Collections;
using UnityEngine;

namespace ChibiRuby.Serializer.Unity
{
    class Vector2Formatter : IMRubyValueFormatter<Vector2>
    {
        public static readonly Vector2Formatter Instance = new();

        public MRubyValue Serialize(Vector2 value, MRubyState mrb, MRubyValueSerializerOptions options)
        {
            var array = mrb.NewArray(2);
            var floatFormatter = options.Resolver.GetFormatterWithVerify<float>();
            var xValue = floatFormatter.Serialize(value.x, mrb, options);
            var yValue = floatFormatter.Serialize(value.y, mrb, options);
            array.Push(xValue);
            array.Push(yValue);
            return array;
        }

        public Vector2 Deserialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options)
        {
            MRubySerializationException.ThrowIfNotEnoughArrayLength(value, 2, "Vector2", mrb);

            var array = value.As<RArray>();
            var xValue = array[0];
            var yValue = array[1];

            var floatFormatter = options.Resolver.GetFormatterWithVerify<float>();
            var x = floatFormatter.Deserialize(xValue, mrb, options);
            var y = floatFormatter.Deserialize(yValue, mrb, options);
            return new Vector2(x, y);
        }
    }

    class Vector2IntFormatter : IMRubyValueFormatter<Vector2Int>
    {
        public static readonly Vector2IntFormatter Instance = new();

        public MRubyValue Serialize(Vector2Int value, MRubyState mrb, MRubyValueSerializerOptions options)
        {
            var array = mrb.NewArray(2);
            var intFormatter = options.Resolver.GetFormatterWithVerify<int>();
            var xValue = intFormatter.Serialize(value.x, mrb, options);
            var yValue = intFormatter.Serialize(value.y, mrb, options);
            array.Push(xValue);
            array.Push(yValue);
            return array;
        }

        public Vector2Int Deserialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options)
        {
            MRubySerializationException.ThrowIfNotEnoughArrayLength(value, 2, "Vector2Int", mrb);

            var array = value.As<RArray>();
            var xValue = array[0];
            var yValue = array[1];

            var intFormatter = options.Resolver.GetFormatterWithVerify<int>();
            var x = intFormatter.Deserialize(xValue, mrb, options);
            var y = intFormatter.Deserialize(yValue, mrb, options);
            return new Vector2Int(x, y);
        }
    }

    class Vector3Formatter : IMRubyValueFormatter<Vector3>
    {
        public static readonly Vector3Formatter Instance = new();

        public MRubyValue Serialize(Vector3 value, MRubyState mrb, MRubyValueSerializerOptions options)
        {
            var array = mrb.NewArray(3);
            var floatFormatter = options.Resolver.GetFormatterWithVerify<float>();
            var xValue = floatFormatter.Serialize(value.x, mrb, options);
            var yValue = floatFormatter.Serialize(value.y, mrb, options);
            var zValue = floatFormatter.Serialize(value.z, mrb, options);
            array.Push(xValue);
            array.Push(yValue);
            array.Push(zValue);
            return array;
        }

        public Vector3 Deserialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options)
        {
            MRubySerializationException.ThrowIfNotEnoughArrayLength(value, 3, "Vector3", mrb);

            var array = value.As<RArray>();
            var xValue = array[0];
            var yValue = array[1];
            var zValue = array[2];

            var floatFormatter = options.Resolver.GetFormatterWithVerify<float>();
            var x = floatFormatter.Deserialize(xValue, mrb, options);
            var y = floatFormatter.Deserialize(yValue, mrb, options);
            var z = floatFormatter.Deserialize(zValue, mrb, options);
            return new Vector3(x, y, z);
        }
    }

    class Vector3IntFormatter : IMRubyValueFormatter<Vector3Int>
    {
        public static readonly Vector3IntFormatter Instance = new();

        public MRubyValue Serialize(Vector3Int value, MRubyState mrb, MRubyValueSerializerOptions options)
        {
            var array = mrb.NewArray(3);
            var intFormatter = options.Resolver.GetFormatterWithVerify<int>();
            var xValue = intFormatter.Serialize(value.x, mrb, options);
            var yValue = intFormatter.Serialize(value.y, mrb, options);
            var zValue = intFormatter.Serialize(value.z, mrb, options);
            array.Push(xValue);
            array.Push(yValue);
            array.Push(zValue);
            return array;
        }

        public Vector3Int Deserialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options)
        {
            MRubySerializationException.ThrowIfNotEnoughArrayLength(value, 3, "Vector3Int", mrb);

            var array = value.As<RArray>();
            var xValue = array[0];
            var yValue = array[1];
            var zValue = array[2];

            var intFormatter = options.Resolver.GetFormatterWithVerify<int>();
            var x = intFormatter.Deserialize(xValue, mrb, options);
            var y = intFormatter.Deserialize(yValue, mrb, options);
            var z = intFormatter.Deserialize(zValue, mrb, options);
            return new Vector3Int(x, y, z);
        }
    }

    class Vector4Formatter : IMRubyValueFormatter<Vector4>
    {
        public static readonly Vector4Formatter Instance = new();

        public MRubyValue Serialize(Vector4 value, MRubyState mrb, MRubyValueSerializerOptions options)
        {
            var array = mrb.NewArray(4);
            var floatFormatter = options.Resolver.GetFormatterWithVerify<float>();
            var xValue = floatFormatter.Serialize(value.x, mrb, options);
            var yValue = floatFormatter.Serialize(value.y, mrb, options);
            var zValue = floatFormatter.Serialize(value.z, mrb, options);
            var wValue = floatFormatter.Serialize(value.w, mrb, options);
            array.Push(xValue);
            array.Push(yValue);
            array.Push(zValue);
            array.Push(wValue);
            return array;
        }

        public Vector4 Deserialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options)
        {
            MRubySerializationException.ThrowIfNotEnoughArrayLength(value, 4, "Vector4", mrb);

            var array = value.As<RArray>();
            var xValue = array[0];
            var yValue = array[1];
            var zValue = array[2];
            var wValue = array[3];

            var floatFormatter = options.Resolver.GetFormatterWithVerify<float>();
            var x = floatFormatter.Deserialize(xValue, mrb, options);
            var y = floatFormatter.Deserialize(yValue, mrb, options);
            var z = floatFormatter.Deserialize(zValue, mrb, options);
            var w = floatFormatter.Deserialize(wValue, mrb, options);
            return new Vector4(x, y, z, w);
        }
    }

    class ColorFormatter : IMRubyValueFormatter<Color>
    {
        public static readonly ColorFormatter Instance = new();

        public MRubyValue Serialize(Color value, MRubyState mrb, MRubyValueSerializerOptions options)
        {
            var array = mrb.NewArray(4);
            var floatFormatter = options.Resolver.GetFormatterWithVerify<float>();
            var rValue = floatFormatter.Serialize(value.r, mrb, options);
            var gValue = floatFormatter.Serialize(value.g, mrb, options);
            var bValue = floatFormatter.Serialize(value.b, mrb, options);
            var aValue = floatFormatter.Serialize(value.a, mrb, options);
            array.Push(rValue);
            array.Push(gValue);
            array.Push(bValue);
            array.Push(aValue);
            return array;
        }

        public Color Deserialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options)
        {
            MRubySerializationException.ThrowIfNotEnoughArrayLength(value, 4, "Color", mrb);

            var array = value.As<RArray>();
            var rValue = array[0];
            var gValue = array[1];
            var bValue = array[2];
            var aValue = array[3];

            var floatFormatter = options.Resolver.GetFormatterWithVerify<float>();
            var r = floatFormatter.Deserialize(rValue, mrb, options);
            var g = floatFormatter.Deserialize(gValue, mrb, options);
            var b = floatFormatter.Deserialize(bValue, mrb, options);
            var a = floatFormatter.Deserialize(aValue, mrb, options);
            return new Color(r, g, b, a);
        }
    }

    class Color32Formatter : IMRubyValueFormatter<Color32>
    {
        public static readonly Color32Formatter Instance = new();

        public MRubyValue Serialize(Color32 value, MRubyState mrb, MRubyValueSerializerOptions options)
        {
            var array = mrb.NewArray(4);
            var byteFormatter = options.Resolver.GetFormatterWithVerify<byte>();
            var rValue = byteFormatter.Serialize(value.r, mrb, options);
            var gValue = byteFormatter.Serialize(value.g, mrb, options);
            var bValue = byteFormatter.Serialize(value.b, mrb, options);
            var aValue = byteFormatter.Serialize(value.a, mrb, options);
            array.Push(rValue);
            array.Push(gValue);
            array.Push(bValue);
            array.Push(aValue);
            return array;
        }

        public Color32 Deserialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options)
        {
            MRubySerializationException.ThrowIfNotEnoughArrayLength(value, 4, "Color32", mrb);

            var array = value.As<RArray>();
            var rValue = array[0];
            var gValue = array[1];
            var bValue = array[2];
            var aValue = array[3];

            var byteFormatter = options.Resolver.GetFormatterWithVerify<byte>();
            var r = byteFormatter.Deserialize(rValue, mrb, options);
            var g = byteFormatter.Deserialize(gValue, mrb, options);
            var b = byteFormatter.Deserialize(bValue, mrb, options);
            var a = byteFormatter.Deserialize(aValue, mrb, options);
            return new Color32(r, g, b, a);
        }
    }

    class ResolutionFormatter : IMRubyValueFormatter<Resolution>
    {
        public static readonly ResolutionFormatter Instance = new();

        public MRubyValue Serialize(Resolution value, MRubyState mrb, MRubyValueSerializerOptions options)
        {
            var array = mrb.NewArray(2);
            var intFormatter = options.Resolver.GetFormatterWithVerify<int>();
            var wValue = intFormatter.Serialize(value.width, mrb, options);
            var hValue = intFormatter.Serialize(value.height, mrb, options);
            array.Push(wValue);
            array.Push(hValue);
            return array;
        }

        public Resolution Deserialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options)
        {
            MRubySerializationException.ThrowIfNotEnoughArrayLength(value, 2, "Resolution", mrb);

            var array = value.As<RArray>();
            var wValue = array[0];
            var hValue = array[1];

            var intFormatter = options.Resolver.GetFormatterWithVerify<int>();
            var w = intFormatter.Deserialize(wValue, mrb, options);
            var h = intFormatter.Deserialize(hValue, mrb, options);
            return new Resolution { width = w, height = h };
        }
    }

    class RectFormatter : IMRubyValueFormatter<Rect>
    {
        public static readonly RectFormatter Instance = new();

        public MRubyValue Serialize(Rect value, MRubyState mrb, MRubyValueSerializerOptions options)
        {
            var array = mrb.NewArray(4);
            var floatFormatter = options.Resolver.GetFormatterWithVerify<float>();
            var xValue = floatFormatter.Serialize(value.x, mrb, options);
            var yValue = floatFormatter.Serialize(value.y, mrb, options);
            var wValue = floatFormatter.Serialize(value.width, mrb, options);
            var hValue = floatFormatter.Serialize(value.height, mrb, options);
            array.Push(xValue);
            array.Push(yValue);
            array.Push(wValue);
            array.Push(hValue);
            return array;
        }

        public Rect Deserialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options)
        {
            MRubySerializationException.ThrowIfNotEnoughArrayLength(value, 4, "Rect", mrb);

            var array = value.As<RArray>();
            var xValue = array[0];
            var yValue = array[1];
            var wValue = array[2];
            var hValue = array[3];

            var floatFormatter = options.Resolver.GetFormatterWithVerify<float>();
            var x = floatFormatter.Deserialize(xValue, mrb, options);
            var y = floatFormatter.Deserialize(yValue, mrb, options);
            var w = floatFormatter.Deserialize(wValue, mrb, options);
            var h = floatFormatter.Deserialize(hValue, mrb, options);
            return new Rect(x, y, w, h);
        }
    }

    class RectIntFormatter : IMRubyValueFormatter<RectInt>
    {
        public static readonly RectIntFormatter Instance = new();

        public MRubyValue Serialize(RectInt value, MRubyState mrb, MRubyValueSerializerOptions options)
        {
            var array = mrb.NewArray(4);
            var intFormatter = options.Resolver.GetFormatterWithVerify<int>();
            var xValue = intFormatter.Serialize(value.x, mrb, options);
            var yValue = intFormatter.Serialize(value.y, mrb, options);
            var wValue = intFormatter.Serialize(value.width, mrb, options);
            var hValue = intFormatter.Serialize(value.height, mrb, options);
            array.Push(xValue);
            array.Push(yValue);
            array.Push(wValue);
            array.Push(hValue);
            return array;
        }

        public RectInt Deserialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options)
        {
            MRubySerializationException.ThrowIfNotEnoughArrayLength(value, 4, "RectInt", mrb);

            var array = value.As<RArray>();
            var xValue = array[0];
            var yValue = array[1];
            var wValue = array[2];
            var hValue = array[3];

            var intFormatter = options.Resolver.GetFormatterWithVerify<int>();
            var x = intFormatter.Deserialize(xValue, mrb, options);
            var y = intFormatter.Deserialize(yValue, mrb, options);
            var w = intFormatter.Deserialize(wValue, mrb, options);
            var h = intFormatter.Deserialize(hValue, mrb, options);
            return new RectInt(x, y, w, h);
        }
    }

    class RectOffsetFormatter : IMRubyValueFormatter<RectOffset>
    {
        public static readonly RectOffsetFormatter Instance = new();

        public MRubyValue Serialize(RectOffset value, MRubyState mrb, MRubyValueSerializerOptions options)
        {
            var array = mrb.NewArray(4);
            var intFormatter = options.Resolver.GetFormatterWithVerify<int>();
            var lValue = intFormatter.Serialize(value.left, mrb, options);
            var rValue = intFormatter.Serialize(value.right, mrb, options);
            var tValue = intFormatter.Serialize(value.top, mrb, options);
            var bValue = intFormatter.Serialize(value.bottom, mrb, options);
            array.Push(lValue);
            array.Push(rValue);
            array.Push(tValue);
            array.Push(bValue);
            return array;
        }

        public RectOffset Deserialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options)
        {
            MRubySerializationException.ThrowIfNotEnoughArrayLength(value, 4, "RectOffset", mrb);

            var array = value.As<RArray>();
            var lValue = array[0];
            var rValue = array[1];
            var tValue = array[2];
            var bValue = array[3];

            var intFormatter = options.Resolver.GetFormatterWithVerify<int>();
            var l = intFormatter.Deserialize(lValue, mrb, options);
            var r = intFormatter.Deserialize(rValue, mrb, options);
            var t = intFormatter.Deserialize(tValue, mrb, options);
            var b = intFormatter.Deserialize(bValue, mrb, options);
            return new RectOffset(l, r, t, b);
        }
    }

    class QuaternionFormatter : IMRubyValueFormatter<Quaternion>
    {
        public static readonly QuaternionFormatter Instance = new();

        public MRubyValue Serialize(Quaternion value, MRubyState mrb, MRubyValueSerializerOptions options)
        {
            var array = mrb.NewArray(4);
            var floatFormatter = options.Resolver.GetFormatterWithVerify<float>();
            var xValue = floatFormatter.Serialize(value.x, mrb, options);
            var yValue = floatFormatter.Serialize(value.y, mrb, options);
            var zValue = floatFormatter.Serialize(value.z, mrb, options);
            var wValue = floatFormatter.Serialize(value.w, mrb, options);
            array.Push(xValue);
            array.Push(yValue);
            array.Push(zValue);
            array.Push(wValue);
            return array;
        }

        public Quaternion Deserialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options)
        {
            MRubySerializationException.ThrowIfNotEnoughArrayLength(value, 4, "Quaternion", mrb);

            var array = value.As<RArray>();
            var xValue = array[0];
            var yValue = array[1];
            var zValue = array[2];
            var wValue = array[3];

            var floatFormatter = options.Resolver.GetFormatterWithVerify<float>();
            var x = floatFormatter.Deserialize(xValue, mrb, options);
            var y = floatFormatter.Deserialize(yValue, mrb, options);
            var z = floatFormatter.Deserialize(zValue, mrb, options);
            var w = floatFormatter.Deserialize(wValue, mrb, options);
            return new Quaternion(x, y, z, w);
        }
    }

    class Matrix4x4Formatter : IMRubyValueFormatter<Matrix4x4>
    {
        public static readonly Matrix4x4Formatter Instance = new();

        public MRubyValue Serialize(Matrix4x4 value, MRubyState mrb, MRubyValueSerializerOptions options)
        {
            var array = mrb.NewArray(4);
            var vector4Formatter = options.Resolver.GetFormatterWithVerify<Vector4>();
            var col0Value = vector4Formatter.Serialize(value.GetColumn(0), mrb, options);
            var col1Value = vector4Formatter.Serialize(value.GetColumn(1), mrb, options);
            var col2Value = vector4Formatter.Serialize(value.GetColumn(2), mrb, options);
            var col3Value = vector4Formatter.Serialize(value.GetColumn(3), mrb, options);
            array.Push(col0Value);
            array.Push(col1Value);
            array.Push(col2Value);
            array.Push(col3Value);
            return array;
        }

        public Matrix4x4 Deserialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options)
        {
            MRubySerializationException.ThrowIfNotEnoughArrayLength(value, 4, "Matrix4x4", mrb);

            var array = value.As<RArray>();
            var col0Value = array[0];
            var col1Value = array[1];
            var col2Value = array[2];
            var col3Value = array[3];

            var vector4Formatter = options.Resolver.GetFormatterWithVerify<Vector4>();
            var col0 = vector4Formatter.Deserialize(col0Value, mrb, options);
            var col1 = vector4Formatter.Deserialize(col1Value, mrb, options);
            var col2 = vector4Formatter.Deserialize(col2Value, mrb, options);
            var col3 = vector4Formatter.Deserialize(col3Value, mrb, options);
            return new Matrix4x4(col0, col1, col2, col3);
        }
    }

    class BoundsFormatter : IMRubyValueFormatter<Bounds>
    {
        public static readonly BoundsFormatter Instance = new();

        public MRubyValue Serialize(Bounds value, MRubyState mrb, MRubyValueSerializerOptions options)
        {
            var array = mrb.NewArray(2);
            var vector3Formatter = options.Resolver.GetFormatterWithVerify<Vector3>();
            var centerValue = vector3Formatter.Serialize(value.center, mrb, options);
            var sizeValue = vector3Formatter.Serialize(value.size, mrb, options);
            array.Push(centerValue);
            array.Push(sizeValue);
            return array;
        }

        public Bounds Deserialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options)
        {
            MRubySerializationException.ThrowIfNotEnoughArrayLength(value, 2, "Bounds", mrb);

            var array = value.As<RArray>();
            var centerValue = array[0];
            var sizeValue = array[1];

            var vector3Formatter = options.Resolver.GetFormatterWithVerify<Vector3>();
            var center = vector3Formatter.Deserialize(centerValue, mrb, options);
            var size = vector3Formatter.Deserialize(sizeValue, mrb, options);
            return new Bounds(center, size);
        }
    }

    class BoundsIntFormatter : IMRubyValueFormatter<BoundsInt>
    {
        public static readonly BoundsIntFormatter Instance = new();

        public MRubyValue Serialize(BoundsInt value, MRubyState mrb, MRubyValueSerializerOptions options)
        {
            var array = mrb.NewArray(2);
            var vector3IntFormatter = options.Resolver.GetFormatterWithVerify<Vector3Int>();
            var positionValue = vector3IntFormatter.Serialize(value.position, mrb, options);
            var sizeValue = vector3IntFormatter.Serialize(value.size, mrb, options);
            array.Push(positionValue);
            array.Push(sizeValue);
            return array;
        }

        public BoundsInt Deserialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options)
        {
            MRubySerializationException.ThrowIfNotEnoughArrayLength(value, 2, "BoundsInt", mrb);

            var array = value.As<RArray>();
            var positionValue = array[0];
            var sizeValue = array[1];

            var vector3IntFormatter = options.Resolver.GetFormatterWithVerify<Vector3Int>();
            var position = vector3IntFormatter.Deserialize(positionValue, mrb, options);
            var size = vector3IntFormatter.Deserialize(sizeValue, mrb, options);
            return new BoundsInt(position, size);
        }
    }

     class NativeArrayFormatter<T> : IMRubyValueFormatter<NativeArray<T>> where T : struct
     {
         public MRubyValue Serialize(NativeArray<T> value, MRubyState mrb, MRubyValueSerializerOptions options)
         {
             if (!value.IsCreated) return default;

             var array = mrb.NewArray(value.Length);
             foreach (var x in value)
             {
                 var elementValue = options.Resolver.GetFormatterWithVerify<T>()
                     .Serialize(x, mrb, options);
                 array.Push(elementValue);
             }
             return array;
         }

         public NativeArray<T> Deserialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options)
         {
             if (value.IsNil) return default;
             MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array);

             var array = value.As<RArray>();
             var result = new NativeArray<T>(array.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
             for (var i = 0; i < result.Length; i++)
             {
                 var elementValue = array[i];
                 var element = options.Resolver.GetFormatterWithVerify<T>()
                     .Deserialize(elementValue, mrb, options);
                 result[i] = element;
             }
             return result;
         }
     }
}