﻿using System.Collections;
using System.Reflection;
using UnityEditor;
using System.Linq;
using System;

namespace UniEasy.Editor
{
    public static partial class SerializedPropertyExtensions
    {
        #region Static Fields

        private static MethodInfo getHandler;
        private static PropertyInfo propertyDrawer;

        private const string PropertyDrawerStr = "propertyDrawer";
        private const string ArrayDataStr = ".Array.data[";
        private const string GetHandlerStr = "GetHandler";
        private const string RightBracketStr = "]";
        private const string LeftBracketStr = "[";
        private const string EmptyStr = "";
        private const char StopChar = '.';

        #endregion

        #region Static Methods

        private static object GetValue(object source, string name)
        {
            if (source == null)
            {
                return null;
            }
            Type type = source.GetType();
            FieldInfo field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (field == null)
            {
                PropertyInfo property = type.GetProperty(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (property == null)
                {
                    return null;
                }
                return property.GetValue(source, null);
            }
            return field.GetValue(source);
        }

        private static object GetValue(object source, string name, int index)
        {
            var enumerable = GetValue(source, name) as IEnumerable;
            if (enumerable == null)
            {
                return null;
            }
            var enm = enumerable.GetEnumerator();
            while (index-- >= 0)
            {
                enm.MoveNext();
            }
            return enm.Current;
        }

        public static T GetValue<T>(this SerializedProperty property)
        {
            var path = property.propertyPath.Replace(ArrayDataStr, LeftBracketStr);
            object obj = property.serializedObject.targetObject;
            var elements = path.Split(StopChar);
            foreach (var element in elements)
            {
                if (element.Contains(LeftBracketStr))
                {
                    var elementName = element.Substring(0, element.IndexOf(LeftBracketStr));
                    var index = Convert.ToInt32(element.Substring(element.IndexOf(LeftBracketStr)).Replace(LeftBracketStr, EmptyStr).Replace(RightBracketStr, EmptyStr));
                    obj = GetValue(obj, elementName, index);
                }
                else
                {
                    obj = GetValue(obj, element);
                }
            }
            if (obj is T)
            {
                return (T)obj;
            }
            return default;
        }

        public static SerializedProperty GetBelongArrayAndIndex(this SerializedProperty property, out int index)
        {
            index = -1;
            var path = property.propertyPath.Replace(ArrayDataStr, LeftBracketStr);
            var obj = property.serializedObject.targetObject;
            var elements = path.Split(StopChar);
            foreach (var element in elements)
            {
                if (element.Contains(LeftBracketStr))
                {
                    var elementName = element.Substring(0, element.IndexOf(LeftBracketStr));
                    index = Convert.ToInt32(element.Substring(element.IndexOf(LeftBracketStr)).Replace(LeftBracketStr, EmptyStr).Replace(RightBracketStr, EmptyStr));
                    return property.serializedObject.FindProperty(elementName);
                }
            }
            return property;
        }

        public static T GetParent<T>(this SerializedProperty property)
        {
            var path = property.propertyPath.Replace(ArrayDataStr, LeftBracketStr);
            var obj = (object)property.serializedObject.targetObject;
            var elements = path.Split(StopChar);
            foreach (var element in elements.Take(elements.Length - 1))
            {
                if (element.Contains(LeftBracketStr))
                {
                    var elementName = element.Substring(0, element.IndexOf(LeftBracketStr));
                    var index = Convert.ToInt32(element.Substring(element.IndexOf(LeftBracketStr)).Replace(LeftBracketStr, EmptyStr).Replace(RightBracketStr, EmptyStr));
                    obj = GetValue(obj, elementName, index);
                }
                else
                {
                    obj = GetValue(obj, element);
                }
            }
            return (T)obj;
        }

        public static Type GetTypeReflection(this SerializedProperty property)
        {
            object obj = GetParent<object>(property);
            if (obj == null)
            {
                return null;
            }
            Type type = obj.GetType();
            const BindingFlags bindingFlags = BindingFlags.GetField
                                              | BindingFlags.GetProperty
                                              | BindingFlags.Instance
                                              | BindingFlags.NonPublic
                                              | BindingFlags.Public;
            FieldInfo field = type.GetField(property.name, bindingFlags);
            if (field == null)
            {
                return null;
            }
            return field.FieldType;
        }

        public static string GetRootPath(this SerializedProperty property)
        {
            var rootPath = property.propertyPath;
            var firstDot = property.propertyPath.IndexOf(StopChar);
            if (firstDot > 0)
            {
                rootPath = property.propertyPath.Substring(0, firstDot);
            }
            return rootPath;
        }

        public static object[] GetAttributes<T>(this SerializedProperty property)
        {
            object obj = GetParent<object>(property);
            if (obj == null)
            {
                return null;
            }

            Type attrType = typeof(T);
            Type type = obj.GetType();
            const BindingFlags bindingFlags = BindingFlags.GetField
                                              | BindingFlags.GetProperty
                                              | BindingFlags.Instance
                                              | BindingFlags.NonPublic
                                              | BindingFlags.Public;
            FieldInfo field = type.GetField(property.name, bindingFlags);
            if (field != null)
            {
                return field.GetCustomAttributes(attrType, true);
            }
            return null;
        }

        public static bool HasAttribute<T>(this SerializedProperty property)
        {
            object[] attrs = GetAttributes<T>(property);
            if (attrs != null)
            {
                return attrs.Length > 0;
            }
            return false;
        }

        public static object GetHandler(this SerializedProperty property)
        {
            if (getHandler == null)
            {
                getHandler = TypeHelper.ScriptAttributeUtilityType.GetMethod(GetHandlerStr, BindingFlags.Static | BindingFlags.NonPublic);
            }
            return getHandler.Invoke(null, new object[] { property });
        }

        public static PropertyDrawer TryGetPropertyDrawer(this SerializedProperty property)
        {
            if (propertyDrawer == null)
            {
                propertyDrawer = TypeHelper.PropertyHandlerType.GetProperty(PropertyDrawerStr, BindingFlags.Instance | BindingFlags.NonPublic);
            }
            return (PropertyDrawer)propertyDrawer.GetValue(GetHandler(property), null);
        }

        public static int HashCodeForPropertyPath(this SerializedProperty property)
        {
            // For efficiency, ignore indices inside brackets [] in order to make array elements share handlers.
            int key = property.serializedObject.targetObject.GetInstanceID() ^ HashCodeForPropertyPathWithoutArrayIndex(property);
            if (property.propertyType == SerializedPropertyType.ObjectReference)
            {
                key ^= property.objectReferenceInstanceIDValue;
            }
            return key;
        }

        private static int HashCodeForPropertyPathWithoutArrayIndex(SerializedProperty property)
        {
            return SerializedPropertyHelper.GetHashCodeForPropertyPathWithoutArrayIndex(property);
        }

        #endregion
    }
}
