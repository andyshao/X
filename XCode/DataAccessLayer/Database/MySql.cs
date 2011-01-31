﻿using System;
using System.Data.Common;
using System.IO;
using System.Reflection;
using System.Web;
using System.Data;
using System.Text;
using System.Collections.Generic;

namespace XCode.DataAccessLayer
{
    class MySql : NetDb
    {
        #region 属性
        /// <summary>
        /// 返回数据库类型。
        /// </summary>
        public override DatabaseType DbType
        {
            get { return DatabaseType.MySql; }
        }

        private static DbProviderFactory _dbProviderFactory;
        /// <summary>
        /// 提供者工厂
        /// </summary>
        static DbProviderFactory dbProviderFactory
        {
            get
            {
                if (_dbProviderFactory == null) _dbProviderFactory = GetProviderFactory("MySql.Data.dll", "MySql.Data.MySqlClient.MySqlClientFactory");

                return _dbProviderFactory;
            }
        }

        /// <summary>工厂</summary>
        public override DbProviderFactory Factory
        {
            get { return dbProviderFactory; }
        }
        #endregion

        #region 方法
        /// <summary>
        /// 创建数据库会话
        /// </summary>
        /// <returns></returns>
        protected override IDbSession OnCreateSession()
        {
            return new MySqlSession();
        }

        /// <summary>
        /// 创建元数据对象
        /// </summary>
        /// <returns></returns>
        protected override IMetaData OnCreateMetaData()
        {
            return new MySqlMetaData();
        }
        #endregion

        #region 分页
        /// <summary>
        /// 已重写。获取分页
        /// </summary>
        /// <param name="sql">SQL语句</param>
        /// <param name="startRowIndex">开始行，0开始</param>
        /// <param name="maximumRows">最大返回行数</param>
        /// <param name="keyColumn">主键列。用于not in分页</param>
        /// <returns></returns>
        public override string PageSplit(string sql, Int32 startRowIndex, Int32 maximumRows, string keyColumn)
        {
            // 从第一行开始，不需要分页
            if (startRowIndex <= 0)
            {
                if (maximumRows < 1)
                    return sql;
                else
                    return String.Format("{0} limit {1}", sql, maximumRows);
            }
            if (maximumRows < 1)
                throw new NotSupportedException("不支持取第几条数据之后的所有数据！");
            else
                sql = String.Format("{0} limit {1}, {2}", sql, startRowIndex, maximumRows);
            return sql;
        }

        public override string PageSplit(SelectBuilder builder, int startRowIndex, int maximumRows, string keyColumn)
        {
            return PageSplit(builder.ToString(), startRowIndex, maximumRows, keyColumn);
        }
        #endregion

        #region 数据库特性
        /// <summary>
        /// 当前时间函数
        /// </summary>
        public override String DateTimeNow { get { return "now()"; } }

        /// <summary>
        /// 格式化时间为SQL字符串
        /// </summary>
        /// <param name="dateTime">时间值</param>
        /// <returns></returns>
        public override String FormatDateTime(DateTime dateTime)
        {
            return String.Format("'{0:yyyy-MM-dd HH:mm:ss}'", dateTime);
        }

        /// <summary>
        /// 格式化关键字
        /// </summary>
        /// <param name="keyWord">关键字</param>
        /// <returns></returns>
        public override String FormatKeyWord(String keyWord)
        {
            if (String.IsNullOrEmpty(keyWord)) throw new ArgumentNullException("keyWord");

            if (keyWord.StartsWith("`") && keyWord.EndsWith("`")) return keyWord;

            return String.Format("`{0}`", keyWord);
        }

        /// <summary>
        /// 格式化数据为SQL数据
        /// </summary>
        /// <param name="field"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public override string FormatValue(XField field, object value)
        {
            if (field.DataType == typeof(String))
            {
                if (value == null) return field.Nullable ? "null" : "``";
                if (String.IsNullOrEmpty(value.ToString()) && field.Nullable) return "null";
                return "`" + value + "`";
            }

            return base.FormatValue(field, value);
        }

        /// <summary>
        /// 长文本长度
        /// </summary>
        public override int LongTextLength { get { return 255; } }

        /// <summary>系统数据库名</summary>
        public override String SystemDatabaseName { get { return "mysql"; } }
        #endregion
    }

    /// <summary>
    /// MySql数据库
    /// </summary>
    internal class MySqlSession : NetDbSession
    {
        #region 基本方法 查询/执行
        /// <summary>
        /// 执行插入语句并返回新增行的自动编号
        /// </summary>
        /// <param name="sql">SQL语句</param>
        /// <returns>新增行的自动编号</returns>
        public override Int64 InsertAndGetIdentity(string sql)
        {
            ExecuteTimes++;
            //SQLServer写法
            sql = "SET NOCOUNT ON;" + sql + ";Select LAST_INSERT_ID()";
            if (Debug) WriteLog(sql);
            try
            {
                DbCommand cmd = PrepareCommand();
                cmd.CommandText = sql;
                return Int64.Parse(cmd.ExecuteScalar().ToString());
            }
            catch (DbException ex)
            {
                throw OnException(ex, sql);
            }
            finally { AutoClose(); }
        }
        #endregion
    }

    /// <summary>
    /// MySql元数据
    /// </summary>
    class MySqlMetaData : NetDbMetaData
    {
        protected override void FixField(XField field, DataRow dr)
        {
            // 修正原始类型
            String rawType = null;
            if (TryGetDataRowValue<String>(dr, "COLUMN_TYPE", out rawType)) field.RawType = rawType;

            // 修正自增字段
            String extra = null;
            if (TryGetDataRowValue<String>(dr, "EXTRA", out extra) && extra == "auto_increment") field.Identity = true;

            base.FixField(field, dr);
        }

        protected override DataRow[] FindDataType(XField field, string typeName, bool? isLong)
        {
            DataRow[] drs = base.FindDataType(field, typeName, isLong);
            if (drs != null && drs.Length > 1)
            {
                // 无符号/有符号
                if (!String.IsNullOrEmpty(field.RawType))
                {
                    Boolean IsUnsigned = field.RawType.ToLower().Contains("unsigned");

                    foreach (DataRow dr in drs)
                    {
                        String format = GetDataRowValue<String>(dr, "CreateFormat");

                        if (IsUnsigned && format.ToLower().Contains("unsigned"))
                            return new DataRow[] { dr };
                        else if (!IsUnsigned && !format.ToLower().Contains("unsigned"))
                            return new DataRow[] { dr };
                    }
                }
            }
            return drs;
        }

        public override string FieldClause(XField field, bool onlyDefine)
        {
            String sql = base.FieldClause(field, onlyDefine);
            // 加上注释
            if (!String.IsNullOrEmpty(field.Description)) sql = String.Format("{0} COMMENT '{1}'", sql, field.Description);
            return sql;
        }

        protected override string GetFieldConstraints(XField field, Boolean onlyDefine)
        {
            String str = null;
            if (!field.Nullable) str = "NOT NULL";

            if (field.Identity) str = " NOT NULL AUTO_INCREMENT";

            return str;
        }

        //protected override void SetFieldType(XField field, string typeName)
        //{
        //    DataTable dt = DataTypes;
        //    if (dt == null) return;

        //    DataRow[] drs = FindDataType(field, typeName, null);
        //    if (drs == null || drs.Length < 1) return;

        //    // 修正原始类型
        //    String rawType = null;
        //    if (TryGetDataRowValue<String>(drs[0], "COLUMN_TYPE", out rawType)) field.RawType = rawType;

        //    base.SetFieldType(field, typeName);
        //}

        #region 架构定义
        public override object SetSchema(DDLSchema schema, params object[] values)
        {
            if (schema == DDLSchema.DatabaseExist)
            {
                IDbSession session = Database.CreateSession();

                DataTable dt = GetSchema("Databases", new String[] { values != null && values.Length > 0 ? (String)values[0] : session.DatabaseName });
                if (dt == null || dt.Rows == null || dt.Rows.Count < 1) return false;
                return true;
            }

            return base.SetSchema(schema, values);
        }

        public override string DropDatabaseSQL(string dbname)
        {
            return String.Format("Drop Database If Exists {0}", FormatKeyWord(dbname));
        }

        //public override string DatabaseExistSQL(string dbname)
        //{
        //    return String.Format("Select * From db Where 1=0", dbname);
        //}

        public override String CreateTableSQL(XTable table)
        {
            List<XField> Fields = new List<XField>(table.Fields);
            Fields.Sort(delegate(XField item1, XField item2) { return item1.ID.CompareTo(item2.ID); });

            StringBuilder sb = new StringBuilder();
            String key = null;

            sb.AppendFormat("Create Table If Not Exists {0}(", FormatKeyWord(table.Name));
            for (Int32 i = 0; i < Fields.Count; i++)
            {
                sb.AppendLine();
                sb.Append("\t");
                sb.Append(FieldClause(Fields[i], true));
                if (i < Fields.Count - 1) sb.Append(",");

                if (Fields[i].PrimaryKey) key = Fields[i].Name;
            }
            if (!String.IsNullOrEmpty(key))
            {
                sb.AppendLine(",");
                sb.AppendFormat("Primary Key ({0})", FormatKeyWord(key));
            }
            sb.AppendLine();
            sb.Append(")");

            return sb.ToString();
        }

        public override string AddTableDescriptionSQL(XTable table)
        {
            if (String.IsNullOrEmpty(table.Description)) return null;

            return String.Format("Alter Table {0} Comment '{1}'", FormatKeyWord(table.Name), table.Description);
        }

        public override string AddColumnDescriptionSQL(XField field)
        {
            if (String.IsNullOrEmpty(field.Description)) return null;

            return String.Format("Alter Table {0} Modify {1} Comment '{2}'", FormatKeyWord(field.Table.Name), FormatKeyWord(field.Name), field.Description);
        }
        #endregion
    }
}