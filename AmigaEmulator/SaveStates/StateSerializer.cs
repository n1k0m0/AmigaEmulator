/*
   Copyright 2026 Nils Kopal <Nils.Kopal<at>kopaldev.de

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace AmigaEmulator.Core.SaveStates
{
    /// <summary>
    /// Serializes private component fields whose state is purely data.
    /// </summary>
    public static class StateSerializer
    {
        /// <summary>
        /// Writes all serializable instance fields of an object.
        /// </summary>
        public static void WriteObjectFields(BinaryWriter writer, object instance, params string[] excludedFieldNames)
        {
            FieldInfo[] fields = GetSerializableFields(instance.GetType(), excludedFieldNames);
            writer.Write(fields.Length);
            for (int index = 0; index < fields.Length; index++)
            {
                FieldInfo field = fields[index];
                writer.Write(field.Name);
                WriteValue(writer, field.FieldType, field.GetValue(instance));
            }
        }

        /// <summary>
        /// Restores all serializable instance fields of an object.
        /// </summary>
        public static void ReadObjectFields(BinaryReader reader, object instance, params string[] excludedFieldNames)
        {
            var fieldsByName = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
            FieldInfo[] fields = GetSerializableFields(instance.GetType(), excludedFieldNames);
            for (int index = 0; index < fields.Length; index++)
            {
                fieldsByName[fields[index].Name] = fields[index];
            }

            int count = reader.ReadInt32();
            for (int index = 0; index < count; index++)
            {
                string fieldName = reader.ReadString();
                if (!fieldsByName.TryGetValue(fieldName, out FieldInfo field))
                {
                    throw new InvalidDataException("Savestate field mismatch: " + fieldName);
                }

                AssignFieldValue(instance, field, ReadValue(reader, field.FieldType));
            }
        }

        /// <summary>
        /// Writes a nullable byte array with a length prefix.
        /// </summary>
        public static void WriteByteArray(BinaryWriter writer, byte[] value)
        {
            writer.Write(value != null);
            if (value == null)
            {
                return;
            }

            writer.Write(value.Length);
            writer.Write(value);
        }

        /// <summary>
        /// Reads a nullable byte array written by <see cref="WriteByteArray"/>.
        /// </summary>
        public static byte[] ReadByteArray(BinaryReader reader)
        {
            if (!reader.ReadBoolean())
            {
                return null;
            }

            int length = reader.ReadInt32();
            return reader.ReadBytes(length);
        }

        /// <summary>
        /// Writes a nullable unsigned integer array with a length prefix.
        /// </summary>
        public static void WriteUIntArray(BinaryWriter writer, uint[] value)
        {
            writer.Write(value != null);
            if (value == null)
            {
                return;
            }

            writer.Write(value.Length);
            for (int index = 0; index < value.Length; index++)
            {
                writer.Write(value[index]);
            }
        }

        /// <summary>
        /// Reads a nullable unsigned integer array written by <see cref="WriteUIntArray"/>.
        /// </summary>
        public static uint[] ReadUIntArray(BinaryReader reader)
        {
            if (!reader.ReadBoolean())
            {
                return null;
            }

            int length = reader.ReadInt32();
            var value = new uint[length];
            for (int index = 0; index < value.Length; index++)
            {
                value[index] = reader.ReadUInt32();
            }

            return value;
        }

        /// <summary>
        /// Writes a nullable string using an explicit presence marker.
        /// </summary>
        public static void WriteString(BinaryWriter writer, string value)
        {
            writer.Write(value != null);
            if (value != null)
            {
                writer.Write(value);
            }
        }

        /// <summary>
        /// Reads a nullable string written by <see cref="WriteString"/>.
        /// </summary>
        public static string ReadString(BinaryReader reader)
        {
            return reader.ReadBoolean() ? reader.ReadString() : null;
        }

        /// <summary>
        /// Writes a typed value.
        /// </summary>
        private static void WriteValue(BinaryWriter writer, Type type, object value)
        {
            if (type == typeof(bool)) { writer.Write((bool)value); return; }
            if (type == typeof(byte)) { writer.Write((byte)value); return; }
            if (type == typeof(sbyte)) { writer.Write((sbyte)value); return; }
            if (type == typeof(short)) { writer.Write((short)value); return; }
            if (type == typeof(ushort)) { writer.Write((ushort)value); return; }
            if (type == typeof(int)) { writer.Write((int)value); return; }
            if (type == typeof(uint)) { writer.Write((uint)value); return; }
            if (type == typeof(long)) { writer.Write((long)value); return; }
            if (type == typeof(ulong)) { writer.Write((ulong)value); return; }
            if (type == typeof(float)) { writer.Write((float)value); return; }
            if (type == typeof(double)) { writer.Write((double)value); return; }
            if (type == typeof(string)) { WriteString(writer, (string)value); return; }
            if (type.IsEnum) { writer.Write(Convert.ToInt32(value)); return; }
            if (type.IsArray) { WriteArray(writer, type.GetElementType(), (Array)value); return; }
            if (IsGenericList(type)) { WriteList(writer, type.GetGenericArguments()[0], (IList)value); return; }
            if (IsGenericHashSet(type)) { WriteEnumerable(writer, type.GetGenericArguments()[0], (IEnumerable)value); return; }
            if (IsGenericQueue(type)) { WriteEnumerable(writer, type.GetGenericArguments()[0], (IEnumerable)value); return; }
            if (type.IsValueType) { WriteObjectFields(writer, value); return; }

            writer.Write(value != null);
            if (value != null)
            {
                WriteObjectFields(writer, value);
            }
        }

        /// <summary>
        /// Reads a typed value.
        /// </summary>
        private static object ReadValue(BinaryReader reader, Type type)
        {
            if (type == typeof(bool)) { return reader.ReadBoolean(); }
            if (type == typeof(byte)) { return reader.ReadByte(); }
            if (type == typeof(sbyte)) { return reader.ReadSByte(); }
            if (type == typeof(short)) { return reader.ReadInt16(); }
            if (type == typeof(ushort)) { return reader.ReadUInt16(); }
            if (type == typeof(int)) { return reader.ReadInt32(); }
            if (type == typeof(uint)) { return reader.ReadUInt32(); }
            if (type == typeof(long)) { return reader.ReadInt64(); }
            if (type == typeof(ulong)) { return reader.ReadUInt64(); }
            if (type == typeof(float)) { return reader.ReadSingle(); }
            if (type == typeof(double)) { return reader.ReadDouble(); }
            if (type == typeof(string)) { return ReadString(reader); }
            if (type.IsEnum) { return Enum.ToObject(type, reader.ReadInt32()); }
            if (type.IsArray) { return ReadArray(reader, type.GetElementType()); }
            if (IsGenericList(type)) { return ReadList(reader, type, type.GetGenericArguments()[0]); }
            if (IsGenericHashSet(type)) { return ReadHashSet(reader, type, type.GetGenericArguments()[0]); }
            if (IsGenericQueue(type)) { return ReadQueue(reader, type, type.GetGenericArguments()[0]); }
            if (type.IsValueType)
            {
                object boxed = Activator.CreateInstance(type);
                ReadObjectFields(reader, boxed);
                return boxed;
            }

            if (!reader.ReadBoolean())
            {
                return null;
            }

            object instance = CreateUninitializedInstance(type);
            ReadObjectFields(reader, instance);
            return instance;
        }

        /// <summary>
        /// Writes a nullable array of any supported element type.
        /// </summary>
        private static void WriteArray(BinaryWriter writer, Type elementType, Array array)
        {
            writer.Write(array != null);
            if (array == null)
            {
                return;
            }

            writer.Write(array.Rank);
            for (int dimension = 0; dimension < array.Rank; dimension++)
            {
                writer.Write(array.GetLength(dimension));
            }

            int[] indices = new int[array.Rank];
            WriteArrayElements(writer, elementType, array, indices, 0);
        }

        /// <summary>
        /// Writes array elements recursively so multidimensional arrays keep their shape.
        /// </summary>
        private static void WriteArrayElements(BinaryWriter writer, Type elementType, Array array, int[] indices, int dimension)
        {
            if (dimension == array.Rank)
            {
                WriteValue(writer, elementType, array.GetValue(indices));
                return;
            }

            for (int index = 0; index < array.GetLength(dimension); index++)
            {
                indices[dimension] = index;
                WriteArrayElements(writer, elementType, array, indices, dimension + 1);
            }
        }

        /// <summary>
        /// Reads a nullable array written with <see cref="WriteArray"/>.
        /// </summary>
        private static Array ReadArray(BinaryReader reader, Type elementType)
        {
            if (!reader.ReadBoolean())
            {
                return null;
            }

            int rank = reader.ReadInt32();
            var lengths = new int[rank];
            for (int dimension = 0; dimension < rank; dimension++)
            {
                lengths[dimension] = reader.ReadInt32();
            }

            Array array = Array.CreateInstance(elementType, lengths);
            var indices = new int[rank];
            ReadArrayElements(reader, elementType, array, indices, 0);
            return array;
        }

        /// <summary>
        /// Reads array elements recursively.
        /// </summary>
        private static void ReadArrayElements(BinaryReader reader, Type elementType, Array array, int[] indices, int dimension)
        {
            if (dimension == array.Rank)
            {
                array.SetValue(ReadValue(reader, elementType), indices);
                return;
            }

            for (int index = 0; index < array.GetLength(dimension); index++)
            {
                indices[dimension] = index;
                ReadArrayElements(reader, elementType, array, indices, dimension + 1);
            }
        }

        /// <summary>
        /// Writes a nullable generic list.
        /// </summary>
        private static void WriteList(BinaryWriter writer, Type elementType, IList list)
        {
            writer.Write(list != null);
            if (list == null)
            {
                return;
            }

            writer.Write(list.Count);
            for (int index = 0; index < list.Count; index++)
            {
                WriteValue(writer, elementType, list[index]);
            }
        }

        /// <summary>
        /// Writes a nullable enumerable collection with a count prefix.
        /// </summary>
        private static void WriteEnumerable(BinaryWriter writer, Type elementType, IEnumerable enumerable)
        {
            writer.Write(enumerable != null);
            if (enumerable == null)
            {
                return;
            }

            var values = new List<object>();
            foreach (object value in enumerable)
            {
                values.Add(value);
            }

            writer.Write(values.Count);
            for (int index = 0; index < values.Count; index++)
            {
                WriteValue(writer, elementType, values[index]);
            }
        }

        /// <summary>
        /// Reads a nullable generic list.
        /// </summary>
        private static object ReadList(BinaryReader reader, Type listType, Type elementType)
        {
            if (!reader.ReadBoolean())
            {
                return null;
            }

            int count = reader.ReadInt32();
            IList list = (IList)Activator.CreateInstance(listType, true);
            for (int index = 0; index < count; index++)
            {
                list.Add(ReadValue(reader, elementType));
            }

            return list;
        }

        /// <summary>
        /// Reads a nullable generic hash set.
        /// </summary>
        private static object ReadHashSet(BinaryReader reader, Type setType, Type elementType)
        {
            if (!reader.ReadBoolean())
            {
                return null;
            }

            int count = reader.ReadInt32();
            object set = Activator.CreateInstance(setType, true);
            MethodInfo add = setType.GetMethod("Add", new[] { elementType });
            for (int index = 0; index < count; index++)
            {
                add.Invoke(set, new[] { ReadValue(reader, elementType) });
            }

            return set;
        }

        /// <summary>
        /// Reads a nullable generic queue.
        /// </summary>
        private static object ReadQueue(BinaryReader reader, Type queueType, Type elementType)
        {
            if (!reader.ReadBoolean())
            {
                return null;
            }

            int count = reader.ReadInt32();
            object queue = Activator.CreateInstance(queueType, true);
            MethodInfo enqueue = queueType.GetMethod("Enqueue", new[] { elementType });
            for (int index = 0; index < count; index++)
            {
                enqueue.Invoke(queue, new[] { ReadValue(reader, elementType) });
            }

            return queue;
        }

        /// <summary>
        /// Assigns a field while preserving readonly collection instances where possible.
        /// </summary>
        private static void AssignFieldValue(object instance, FieldInfo field, object value)
        {
            object existing = field.GetValue(instance);
            if (field.IsInitOnly && existing is Array existingArray && value is Array valueArray)
            {
                if (existingArray.Rank == valueArray.Rank && existingArray.Length == valueArray.Length)
                {
                    Array.Copy(valueArray, existingArray, valueArray.Length);
                    return;
                }

                field.SetValue(instance, value);
                return;
            }

            if (field.IsInitOnly && existing is IList existingList && value is IList valueList)
            {
                existingList.Clear();
                for (int index = 0; index < valueList.Count; index++)
                {
                    existingList.Add(valueList[index]);
                }

                return;
            }

            if (field.IsInitOnly && existing != null && value != null && IsGenericQueue(field.FieldType))
            {
                InvokeClearAndCopy(existing, value, "Enqueue");
                return;
            }

            if (field.IsInitOnly && existing != null && value != null && IsGenericHashSet(field.FieldType))
            {
                InvokeClearAndCopy(existing, value, "Add");
                return;
            }

            field.SetValue(instance, value);
        }

        /// <summary>
        /// Creates an object for savestate hydration without requiring a public constructor.
        /// </summary>
        private static object CreateUninitializedInstance(Type type)
        {
            try
            {
                return Activator.CreateInstance(type, true);
            }
            catch (MissingMethodException)
            {
                return RuntimeHelpers.GetUninitializedObject(type);
            }
        }

        /// <summary>
        /// Clears a readonly generic collection and copies all values from another collection.
        /// </summary>
        private static void InvokeClearAndCopy(object target, object source, string addMethodName)
        {
            target.GetType().GetMethod("Clear", Type.EmptyTypes).Invoke(target, null);
            MethodInfo add = target.GetType().GetMethod(addMethodName);
            foreach (object value in (IEnumerable)source)
            {
                add.Invoke(target, new[] { value });
            }
        }

        /// <summary>
        /// Gets deterministic serializable instance fields.
        /// </summary>
        private static FieldInfo[] GetSerializableFields(Type type, params string[] excludedFieldNames)
        {
            var excluded = new HashSet<string>(excludedFieldNames ?? Array.Empty<string>(), StringComparer.Ordinal);
            var fields = new List<FieldInfo>();
            FieldInfo[] allFields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int index = 0; index < allFields.Length; index++)
            {
                FieldInfo field = allFields[index];
                if (field.IsStatic || field.IsLiteral ||
                    Attribute.IsDefined(field, typeof(NonSerializedAttribute)) ||
                    excluded.Contains(field.Name))
                {
                    continue;
                }

                if (typeof(Delegate).IsAssignableFrom(field.FieldType))
                {
                    continue;
                }

                fields.Add(field);
            }

            fields.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
            return fields.ToArray();
        }

        /// <summary>
        /// Returns whether a type is a concrete generic list.
        /// </summary>
        private static bool IsGenericList(Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);
        }

        /// <summary>
        /// Returns whether a type is a concrete generic hash set.
        /// </summary>
        private static bool IsGenericHashSet(Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>);
        }

        /// <summary>
        /// Returns whether a type is a concrete generic queue.
        /// </summary>
        private static bool IsGenericQueue(Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Queue<>);
        }
    }
}
