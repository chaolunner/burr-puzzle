﻿using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using System.Linq;
using System;

namespace UniEasy.Editor
{
    public class RuntimeScriptAttributeUtility
    {
        private struct DrawerKeySet
        {
            public Type Drawer;
            public Type Type;
        }

        #region Static Fields 

        // Internal API members
        internal static Stack<RuntimePropertyDrawer> s_DrawerStack = new Stack<RuntimePropertyDrawer>();
        private static Dictionary<Type, DrawerKeySet> s_DrawerTypeForType = null;
        private static Dictionary<string, List<PropertyAttribute>> s_BuiltinAttributes = null;
        private static RuntimePropertyHandler s_SharedNullHandler = new RuntimePropertyHandler();
        private static RuntimePropertyHandler s_NextHandler = new RuntimePropertyHandler();
        private static RuntimePropertyHandlerCache s_GlobalCache = new RuntimePropertyHandlerCache();
        private static RuntimePropertyHandlerCache s_CurrentCache = null;

        #endregion

        #region Static Properties

        public static RuntimePropertyHandlerCache PropertyHandlerCache
        {
            get
            {
                return s_CurrentCache ?? s_GlobalCache;
            }
            set
            { s_CurrentCache = value; }
        }

        #endregion

        #region Static Methods

        public static void ClearGlobalCache()
        {
            s_GlobalCache.Clear();
        }

        private static void PopulateBuiltinAttributes()
        {
            s_BuiltinAttributes = new Dictionary<string, List<PropertyAttribute>>();

            AddBuiltinAttribute("GUIText", "m_Text", new MultilineAttribute());
            AddBuiltinAttribute("TextMesh", "m_Text", new MultilineAttribute());
            // Example: Make Orthographic Size in Camera component be in range between 0 and 1000
            //AddBuiltinAttribute ("Camera", "m_OrthographicSize", new RangeAttribute (0, 1000));
        }

        private static void AddBuiltinAttribute(string componentTypeName, string propertyPath, PropertyAttribute attr)
        {
            string key = componentTypeName + "_" + propertyPath;
            if (!s_BuiltinAttributes.ContainsKey(key))
            {
                s_BuiltinAttributes.Add(key, new List<PropertyAttribute>());
            }
            s_BuiltinAttributes[key].Add(attr);
        }

        private static List<PropertyAttribute> GetBuiltinAttributes(RuntimeSerializedProperty property)
        {
            if (property.RuntimeSerializedObject.TargetObject == null)
            {
                return null;
            }
            Type t = property.RuntimeSerializedObject.TargetObject.GetType();
            if (t == null)
            {
                return null;
            }
            string attrKey = t.Name + "_" + property.PropertyPath;
            List<PropertyAttribute> attr = null;
            s_BuiltinAttributes.TryGetValue(attrKey, out attr);
            return attr;
        }

        // Called on demand
        public static void BuildDrawerTypeForTypeDictionary()
        {
            s_DrawerTypeForType = new Dictionary<Type, DrawerKeySet>();

            var loadedTypes = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => AssemblyHelper.GetTypesFromAssembly(x)).ToArray();

            foreach (Type type in EditorAssembliesHelper.SubclassesOf(typeof(GUIDrawer)))
            {
                object[] attrs = type.GetCustomAttributes(typeof(RuntimeCustomPropertyDrawer), true);
                foreach (RuntimeCustomPropertyDrawer editor in attrs)
                {
                    //Debug.Log("Base type: " + editor.Type);
                    if (!(s_DrawerTypeForType.ContainsKey(editor.Type) && typeof(PropertyDrawer).IsAssignableFrom(type) && typeof(RuntimePropertyDrawer).IsAssignableFrom(s_DrawerTypeForType[editor.Type].Drawer)))
                    {
                        s_DrawerTypeForType[editor.Type] = new DrawerKeySet()
                        {
                            Drawer = type,
                            Type = editor.Type
                        };
                    }

                    if (!editor.UseForChildren)
                    {
                        continue;
                    }

                    var candidateTypes = loadedTypes.Where(x => x.IsSubclassOf(editor.Type));
                    foreach (var candidateType in candidateTypes)
                    {
                        //Debug.Log("Candidate Type: "+ candidateType);
                        if (s_DrawerTypeForType.ContainsKey(candidateType)
                            && (editor.Type.IsAssignableFrom(s_DrawerTypeForType[candidateType].Type)))
                        {
                            //  Debug.Log("skipping");
                            continue;
                        }

                        //Debug.Log("Setting");
                        s_DrawerTypeForType[candidateType] = new DrawerKeySet()
                        {
                            Drawer = type,
                            Type = editor.Type
                        };
                    }
                }

                attrs = type.GetCustomAttributes(typeof(CustomPropertyDrawer), true);
                foreach (CustomPropertyDrawer editor in attrs)
                {
                    var editorType = CustomPropertyDrawerHelper.GetType(editor);
                    bool useForChildren = CustomPropertyDrawerHelper.UseForChildren(editor);
                    //Debug.Log("Base type: " + editorType);
                    if (!(s_DrawerTypeForType.ContainsKey(editorType) && typeof(PropertyDrawer).IsAssignableFrom(type) && typeof(RuntimePropertyDrawer).IsAssignableFrom(s_DrawerTypeForType[editorType].Drawer)))
                    {
                        s_DrawerTypeForType[editorType] = new DrawerKeySet()
                        {
                            Drawer = type,
                            Type = editorType
                        };
                    }

                    if (!useForChildren)
                    {
                        continue;
                    }

                    var candidateTypes = loadedTypes.Where(x => x.IsSubclassOf(editorType));
                    foreach (var candidateType in candidateTypes)
                    {
                        //Debug.Log("Candidate Type: "+ candidateType);
                        if (s_DrawerTypeForType.ContainsKey(candidateType)
                            && (editorType.IsAssignableFrom(s_DrawerTypeForType[candidateType].Type)))
                        {
                            //  Debug.Log("skipping");
                            continue;
                        }

                        //Debug.Log("Setting");
                        s_DrawerTypeForType[candidateType] = new DrawerKeySet()
                        {
                            Drawer = type,
                            Type = editorType
                        };
                    }
                }
            }
        }

        public static Type GetDrawerTypeForType(Type type)
        {
            if (s_DrawerTypeForType == null)
            {
                BuildDrawerTypeForTypeDictionary();
            }

            DrawerKeySet drawerType;
            s_DrawerTypeForType.TryGetValue(type, out drawerType);
            if (drawerType.Drawer != null)
            {
                return drawerType.Drawer;
            }

            // now check for base generic versions of the drawers...
            if (type.IsGenericType)
            {
                s_DrawerTypeForType.TryGetValue(type.GetGenericTypeDefinition(), out drawerType);
            }
            return drawerType.Drawer;
        }

        private static List<PropertyAttribute> GetFieldAttributes(FieldInfo field)
        {
            if (field == null)
            {
                return null;
            }

            object[] attrs = field.GetCustomAttributes(typeof(PropertyAttribute), true);
            if (attrs != null && attrs.Length > 0)
            {
                return new List<PropertyAttribute>(attrs.Select(attr => attr as PropertyAttribute).OrderBy(attr => -attr.order));
            }
            return null;
        }

        private static FieldInfo GetFieldInfoFromProperty(RuntimeSerializedProperty property, out Type type)
        {
            var classType = GetScriptTypeFromProperty(property);
            if (classType == null)
            {
                type = null;
                return null;
            }

            return GetFieldInfoFromPropertyPath(classType, property.PropertyPath, out type);
        }

        private static Type GetScriptTypeFromProperty(RuntimeSerializedProperty property)
        {
            SerializedProperty scriptProp = property.RuntimeSerializedObject.SerializedObject.FindProperty("m_Script");

            if (property.RuntimeSerializedObject != null && property.RuntimeSerializedObject.Target != null)
            {
                return property.RuntimeSerializedObject.Target.GetType();
            }

            if (scriptProp == null)
            {
                return null;
            }

            MonoScript script = scriptProp.objectReferenceValue as MonoScript;

            if (script == null)
            {
                return null;
            }

            return script.GetClass();
        }

        private static FieldInfo GetFieldInfoFromPropertyPath(Type host, string path, out Type type)
        {
            FieldInfo field = null;
            type = host;
            string[] parts = path.Split('.');
            for (int i = 0; i < parts.Length; i++)
            {
                string member = parts[i];

                // *Here is different from the ScriptAttributeUtility
                if (i < parts.Length - 1 && type.IsArrayOrList())
                {
                    type = type.GetArrayOrListElementType();
                    continue;
                }

                // GetField on class A will not find private fields in base classes to A,
                // so we have to iterate through the base classes and look there too.
                // Private fields are relevant because they can still be shown in the Inspector,
                // and that applies to private fields in base classes too.
                FieldInfo foundField = null;
                for (Type currentType = type; foundField == null && currentType != null; currentType = currentType.BaseType)
                {
                    foundField = currentType.GetField(member, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }

                if (foundField == null)
                {
                    type = null;
                    return null;
                }

                field = foundField;
                type = field.FieldType;
            }
            return field;
        }

        public static RuntimePropertyHandler GetHandler(RuntimeSerializedProperty property, List<PropertyAttribute> attributes)
        {
            if (property == null)
            {
                return s_SharedNullHandler;
            }

            // Don't use custom drawers in debug mode
            if (property.RuntimeSerializedObject.SerializedObject.InspectorMode() != InspectorMode.Normal)
            {
                return s_SharedNullHandler;
            }

            // If the drawer is cached, use the cached drawer
            RuntimePropertyHandler handler = PropertyHandlerCache.GetHandler(property, attributes);
            if (handler != null)
            {
                return handler;
            }

            Type propertyType = null;
            List<PropertyAttribute> attrs = null;
            FieldInfo field = null;

            // Determine if SerializedObject target is a script or a builtin type
            UnityEngine.Object targetObject = property.RuntimeSerializedObject.TargetObject;
            if (targetObject is MonoBehaviour || targetObject is ScriptableObject)
            {
                // For scripts, use reflection to get FieldInfo for the member the property represents
                field = GetFieldInfoFromProperty(property, out propertyType);

                // Use reflection to see if this member has an attribute
                attrs = GetFieldAttributes(field);
            }
            else
            {
                // For builtin types, look if we hardcoded an attribute for this property
                // First initialize the hardcoded properties if not already done
                if (s_BuiltinAttributes == null)
                {
                    PopulateBuiltinAttributes();
                }

                if (attrs == null)
                {
                    attrs = GetBuiltinAttributes(property);
                }
            }

            if (attributes != null)
            {
                if (attrs != null)
                {
                    attributes.AddRange(attrs);
                }
                attrs = attributes.OrderBy(attr => -attr.order).ToList();
            }

            handler = s_NextHandler;

            if (attrs != null)
            {
                for (int i = attrs.Count - 1; i >= 0; i--)
                {
                    handler.HandleAttribute(attrs[i], field, propertyType);
                }
            }

            // Field has no CustomPropertyDrawer attribute with matching drawer so look for default drawer for field type
            if (!handler.HasRuntimePropertyDrawer && propertyType != null)
            {
                handler.HandleDrawnType(propertyType, propertyType, field, null);
            }

            if (handler.Empty)
            {
                PropertyHandlerCache.SetHandler(property, s_SharedNullHandler, attributes);
                handler = s_SharedNullHandler;
            }
            else
            {
                PropertyHandlerCache.SetHandler(property, handler, attributes);
                s_NextHandler = new RuntimePropertyHandler();
            }

            return handler;
        }

        #endregion
    }
}
