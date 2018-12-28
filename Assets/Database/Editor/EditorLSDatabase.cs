#if true
using UnityEngine;
using System.Collections;
using FastCollections;
using UnityEditor;
using System;
using System.Reflection;
using System.Collections.Generic;

namespace Lockstep.Data
{
    //编辑Database的类
    public sealed class EditorLSDatabase : IEditorDatabase
    {
        //初始化数据
        private void InitializeData()
        {
            //数据库类型
            Type databaseType = Database.GetType();
            //数据库基类
            Type foundationType = typeof(LSDatabase);
            //看是否是它的子类
            if (!databaseType.IsSubclassOf(foundationType))
            {
                throw new System.Exception("Database does not inherit from LSDatabase and cannot be edited.");
            }
            HashSet<string> nameCollisionChecker = new HashSet<string>();
            FastList<SortInfo> sortInfos = new FastList<SortInfo>();
            //当当前数据库类型不等于数据库基类的话
            while (databaseType != foundationType)
            {
                //获取类里面所有的字段信息
                FieldInfo[] fields = databaseType.GetFields((BindingFlags)~0);
                //遍历字段信息
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    //获取字段信息的所有特性(RegisterDataAttribute)
                    object[] attributes = field.GetCustomAttributes(typeof(RegisterDataAttribute), false);
                    for (int j = 0; j < attributes.Length; j++)
                    {
                        RegisterDataAttribute registerDataAttribute = attributes[j] as RegisterDataAttribute;
                        //注册数据特性
                        //如果注册数据特性不等于空
                        if (registerDataAttribute != null)
                        {
                            //如果字段不是数组 直接过掉
                            if (!field.FieldType.IsArray)
                            {
                                Debug.LogError("Serialized data field must be array");
                                continue;
                            }

                            //如果字段的类型不是DataItem的子类 直接过滤掉
                            if (!field.FieldType.GetElementType().IsSubclassOf(typeof(DataItem)))
                            {
                                Debug.LogError("Serialized data type must be derived from DataItem");
                                continue;
                            }

                            //得到所有的注册排序特性
                            object[] sortAttributes = field.GetCustomAttributes(typeof(RegisterSortAttribute), false);
                            sortInfos.FastClear();
                            //遍历每一个注册排序特性 添加到sortInfo
                            foreach (object obj in sortAttributes)
                            {
                                RegisterSortAttribute sortAtt = obj as RegisterSortAttribute;
                                if (sortAtt != null)
                                {
                                    sortInfos.Add(new SortInfo(sortAtt.Name, sortAtt.DegreeGetter));
                                }
                            }

                            //创建DataItemInfo
                            DataItemInfo dataInfo = new DataItemInfo(
                                                        field.FieldType.GetElementType(),
                                                        registerDataAttribute.DataName,
                                                        field.Name,
                                                        sortInfos.ToArray()
                                                    );

                            //查看是否有重名的值存储
                            if (nameCollisionChecker.Add(dataInfo.DataName) == false)
                            {
                                throw new System.Exception("Data Name collision detected for '" + dataInfo.DataName + "'.");
                            }

                            //注册DataItemInfo
                            RegisterData(dataInfo);
                            break;
                        }
                    }
                }
                databaseType = databaseType.BaseType;
            }
        }


        public LSDatabase Database { get; private set; }

        private SerializedObject _serializedObject;

        public SerializedObject serializedObject { get { return _serializedObject ?? (_serializedObject = Database.cerealObject()); } }

        public EditorLSDatabaseWindow MainWindow { get; private set; }

        public EditorLSDatabase()
        {
        }

        //编辑数据库的初始化
        public void Initialize(EditorLSDatabaseWindow window, LSDatabase database, out bool valid)
        {
            //设置主界面
            this.MainWindow = window;
            //赋值 数据库
            Database = database;

            //初始化Data
            InitializeData();

            bool isValid = true;
            //遍历每一个DataItemInfo
            for (int i = 0; i < DataItemInfos.Count; i++)
            {
                DataItemInfo info = DataItemInfos[i];
                bool bufferValid;
                //创建数据帮助类
                CreateDataHelper(info, out bufferValid);
                if (!bufferValid)
                {
                    Debug.LogError("Database does not match database type described by the database editor. Make sure Lockstep_Database.asset is the correct database type.");
                    isValid = false;
                    break;
                }
            }
            valid = isValid;
        }

        public void RegisterData(Type targetType, string dataName, string dataFieldName, params SortInfo[] sorts)
        {
            DataItemInfos.Add(new DataItemInfo(targetType, dataName, dataFieldName, sorts));
        }

        //注册DataItemInfo
        public void RegisterData(DataItemInfo info)
        {
            DataItemInfos.Add(info);
        }

        //创建DataHelper(数据帮助类)
        private DataHelper CreateDataHelper(DataItemInfo info, out bool valid)
        {
            //创建DataHelper
            DataHelper helper = new DataHelper(info.TargetType, this, Database, info.DataName, info.FieldName, info.Sorts, out valid);
            //添加到帮助次序里面
            this.HelperOrder.Add(info.DataName);
            //建立显示名字和datahelper的关联
            this.DataHelpers.Add(info.DataName, helper);
            return helper;
        }

        //存储了Database里面的所有字段的数据
        private readonly FastList<DataItemInfo> DataItemInfos = new FastList<DataItemInfo>();
        public readonly FastList<string> HelperOrder = new FastList<string>();
        public readonly Dictionary<string, DataHelper> DataHelpers = new Dictionary<string, DataHelper>();
        static bool isSearching;
        static string lastSearchString;
        static string searchString = "";

        public static bool foldAll { get { return foldAllBuffer == true || foldAllBufferBuffer == true; } }

        private static bool foldAllBuffer;
        private static bool foldAllBufferBuffer;
        private int selectedHelperIndex;                    //当前选中的数据帮助类的索引

        public void Draw()
        {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.BeginHorizontal();
            //如果数据帮助类的数量为0的话 直接返回
            if (DataHelpers.Count == 0)
            {
                EditorGUILayout.LabelField("Nothin' here to see");
                return;
            }
            for (int i = 0; i < DataHelpers.Count; i++)
            {
                if (GUILayout.Button(DataHelpers[HelperOrder[i]].DisplayName))
                {
                    selectedHelperIndex = i;
                }
            }
            EditorGUILayout.EndHorizontal();
            //绘制数据库
            DrawDatabase(DataHelpers[HelperOrder[selectedHelperIndex]]);
            EditorGUILayout.EndVertical();
        }

        //绘制数据库
        private static void DrawDatabase(DataHelper dataHelper)
        {
            //如果dataHelper为空 或者dataHelper的dataProperty为空 直接返回
            if (dataHelper == null || dataHelper.DataProperty == null) return;
            dataHelper.serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            //第一个参数为 字段的属性,第二个参数为列表的展现方式
            //绘制list
            LSEditorUtility.ListField(dataHelper.DataProperty, dataHelper.ListFlags);

            //保存修改过的变量
            if (EditorGUI.EndChangeCheck())
            {
                dataHelper.serializedObject.ApplyModifiedProperties();
            }
            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginHorizontal();
            //folding all
            foldAllBufferBuffer = foldAllBuffer;
            foldAllBuffer = false;
            if (GUILayout.Button("Fold All", GUILayout.MaxWidth(80)))
            {
                FoldAll();
            }

            //TODO: Prevent search from modifying data... only modifying display of data
            //Search
            if (dataHelper.DataAttribute.UseFilter)
            {
                EditorGUILayout.LabelField("Filter: ", GUILayout.MaxWidth(35));
                searchString = EditorGUILayout.TextField(searchString, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("X", GUILayout.MaxWidth(20)))
                {
                    searchString = "";
                }
                if (lastSearchString != searchString)
                {
                    if (string.IsNullOrEmpty(searchString) == false)
                    {
                        dataHelper.FilterWithString(searchString);
                    }
                    lastSearchString = searchString;
                }
            }
            else
            {
                EditorGUILayout.LabelField("Filter Disabled", GUILayout.MaxWidth(150));

            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Order Alphabetically"))
            {
                dataHelper.Sort((a1, a2) => a1.Name.CompareTo(a2.Name));
            }

            SortInfo[] sorts = dataHelper.Sorts;
            for (int i = 0; i < sorts.Length; i++)
            {
                SortInfo sort = sorts[i];
                if (GUILayout.Button(sort.sortName))
                {
                    dataHelper.Sort((a1, a2) => sort.degreeGetter(a1) - sort.degreeGetter(a2));
                }
            }

            EditorGUILayout.EndHorizontal();

            dataHelper.Manage();
            dataHelper.serializedObject.Update();

            EditorGUILayout.Space();

            /*if (GUILayout.Button ("Apply")) {
                dataHelper.SourceEditor.Apply ();
            }*/
        }

        public void Save()
        {
            serializedObject.ApplyModifiedProperties();
        }

        public static void FoldAll()
        {
            foldAllBuffer = true;
        }


    }
}
#endif