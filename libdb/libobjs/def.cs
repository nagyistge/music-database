﻿using System;
using System.Collections.Generic;
using System.Collections;
using System.Text;
using System.Reflection;
using libdb;
using System.ComponentModel;

namespace libdb
{
    internal delegate int ConversionHandler(object obj, object in_value, out object out_value, bool is_to_db);

    internal enum data_type
    {
        text,
        number,
        boolean,
        number_array,
    }

    public enum Tables
    {
        tblAlbum,
        tblArtist,
        tblArtistName,
        tblArtistType,
        tblGenre,
        tblIDRedirection,
        tblLabel,
        tblPiece,
        tblTrack,
    }

    public enum AlbumType
    {
        ComposerAlbum = 1,
        ArtistAlbum,
        VariousArtistAlbum
    }


    public abstract class libobj
    {
        [ReadOnly(true)]
        public virtual int ID { get; set; }

        protected libobj old_values { get; set; }

        /// <summary>
        /// Change modification date on a record
        /// </summary>
        /// <param name="table">the table in which the record is located</param>
        /// <param name="id">the primary key value for the record</param>
        public static void Touch(Tables table, int id)
        {
            Database.ExecuteNonQuery(string.Format(
                "UPDATE {0} SET modified_date = '' WHERE id = {1}", table.ToString(), id));
        }
        public static bool Exists(Tables table, int id)
        {
            return !string.IsNullOrEmpty((string)Database.GetScalar(string.Format(
                "SELECT id FROM {0} WHERE id = {1} LIMIT 1", table.ToString(), id)));
        }


        protected libobj() 
        { 
            ID = 0; 
        }

        public virtual void Fill(int id)
        {
            Fill(id, true);
        }

        // the "createClone" param is to avoid infinite loop...
        private void Fill(int id, bool createClone)
        {
            if (!db_structure.IsAutoupdate(this)) return;
        
            if (!Exists(db_structure.GetTable(this), id))
            {
                this.ID = 0;
                return;
                //throw new Exception(string.Format(
                //    "id = {0} in {1} is not found; Fill() failed",
                //    id, db_structure.GetTableName(this)));
            }

            // make sql query string...
            string str = String.Format("SELECT {2} FROM {0} WHERE id = {1}",
                db_structure.GetTableName(this), id,
                string.Join(",", db_structure.GetFieldNames(this)));

            try
            {
                // get values from db
                ArrayList values = Database.GetFirstRow(str);

                ConversionHandler[] handler = (db_structure.GetConversionHandlers(this));
                PropertyInfo[] ps = db_structure.PropertiesToBeUpdated(this);
                object outv;

                // try type casting to the properties
                for (int i = 0; i < values.Count; i++)
                {
                    //try the specific conversion handler first
                    if (handler[i] != null && (handler[i].Invoke(this, values[i], out outv, false) == 0))
                    {
                        ps[i].SetValue(this, outv, null);
                        continue;
                    }

                    //otherwise try generalized method below
                    if (convert_from_db_datatype(ps[i].PropertyType.ToString(), values[i], out outv) != 0)
                    {
                        //conversion fails
                        throw new Exception(string.Format(
                            "Data type conversion from {1} failed", ps[i].PropertyType.ToString()));
                    }

                    ps[i].SetValue(this, outv, null);
                  }
                this.ID = id;
                if (createClone) this.old_values = Clone();
            }
            catch (SqliteClient.SQLiteException e)
            {
                throw new Exception("Database connection returns error",e);
            }
        }
        /// <summary>
        /// Just a convenient alias for Insert() or Update(): Insert() if this.ID == 0; else Update()
        /// </summary>
        public virtual void Commit()
        {
            if (ID == 0) Insert();
            else Update();
        }
        public virtual void Insert()
        {
            if (!db_structure.IsAutoupdate(this)) return;

            if (this.ID != 0)
                throw new Exception(string.Format(
                    "id = {0} on {1} already exists; insert failed", this.ID, this.GetType().Name));

            string sql = make_insert_sql();
            if (string.IsNullOrEmpty(sql)) return;
            
            Database.ExecuteNonQuery(sql);
            this.ID = Database.LastInsertRowID();

            Database.WriteLog(sql, make_delete_sql());
            old_values = Clone();
        }
        public virtual void Update()
        {
            if (!db_structure.IsAutoupdate(this)) 
                return;
            if (this.ID == 0)
                throw new Exception("The record does not exist in database; try Insert() instead of Update()");

            ArrayList oldv = old_values.get_values_for_db();
            ArrayList newv = get_values_for_db();

            if (IsEquivalent(oldv,newv)) //nothing changed; no update needed
                return;

            string sql = make_update_sql(newv);
            if (string.IsNullOrEmpty(sql)) return;
            
            Database.ExecuteNonQuery(sql);

            Database.WriteLog(sql, old_values.make_update_sql(oldv));
            old_values = Clone();
        }
        public virtual void Delete()
        {
            string sql = make_delete_sql();
            Database.ExecuteNonQuery(sql);
            if (Exists(db_structure.GetTable(this), this.ID))
                return; // delete failed; don't do anything else...
            
            this.ID = 0; // delete successful!
            Database.WriteLog(sql, old_values.make_insert_sql());

        }

        /// <summary>
        /// Will try the best to determine whether there is constraint preventing this record being deleted.
        /// </summary>
        /// <returns></returns>
        public virtual bool IsDeletable()
        {
            // TODO
            return true;
        }
        
        
        public virtual libobj Clone()
        {
            libobj o = (libobj)System.Activator.CreateInstance(this.GetType());
            o.Fill(this.ID, false);
            return o;
        }

        protected virtual bool IsEquivalent(ArrayList oldv, ArrayList newv)
        {
            if (oldv.Count != newv.Count) return false;

            for (int i = 0; i < oldv.Count; i++)
                if (oldv[i].ToString() != newv[i].ToString()) return false;

            return true;
        }

        public virtual bool IsEquivalent(libobj obj)
        {
            if (!db_structure.IsAutoupdate(this) ||
                !db_structure.IsAutoupdate(obj))
                return this == obj;

            return IsEquivalent(this.get_values_for_db(),obj.get_values_for_db());
            //PropertyInfo[] ps = db_structure.PropertiesToBeUpdated(this);
            //data_type[] datatypes = db_structure.GetDataTypes(this);
            //bool[] writable = db_structure.GetIsWritable(this);

            //for (int i = 0; i < ps.Length; i++)
            //{
            //    if (!writable[i]) continue; //Readonly field should not be changed

            //    if (is_same(datatypes[i], ps[i].GetValue(this, null), ps[i].GetValue(this.old_values, null)))
            //        continue;
            //    else
            //        return false;
            //}

            //return true;
        }
        private string make_update_sql()
        {
            return make_update_sql(get_values_for_db());
        }
        private string make_update_sql(ArrayList values)
        {
            //ArrayList values = get_values_for_db();

            if (values == null || values.Count == 0)
                return null; //nothing to update...

            // get the right format for update...
            string[] _columns = db_structure.CleanUpForWrite(this, db_structure.GetFieldNames(this));
            string[] _values = (string[])values.ToArray(typeof(string));
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < _columns.Length; i++)
                sb.Append(_columns[i] + " = " + _values[i] + ",");

            if (sb.Length == 0)
                return null; //nothing to update, again...
            sb.Remove(sb.Length - 1, 1); //removing ending ","

            return String.Format("UPDATE {0} SET {1} WHERE id = {2}",
                db_structure.GetTableName(this), sb.ToString(), this.ID.ToString());
        }
        private string make_insert_sql()
        {
            return make_insert_sql(get_values_for_db());
        }
        private string make_insert_sql(ArrayList values)
        {
            //ArrayList values = get_values_for_db();

            if (values == null) return null;

            return String.Format("INSERT INTO {0} ({1}) VALUES ({2})",
                db_structure.GetTableName(this),
                string.Join(",", db_structure.CleanUpForWrite(this, db_structure.GetFieldNames(this))),
                string.Join(",", (string[])values.ToArray(typeof(string))));
        }
        private string make_delete_sql()
        {
            return string.Format("DELETE FROM {0} WHERE id = {1}",
                db_structure.GetTableName(this), this.ID);
        }
        private ArrayList get_values_for_db()
        {
            try
            {
                ConversionHandler[] handler = (db_structure.GetConversionHandlers(this));
                PropertyInfo[] ps = db_structure.PropertiesToBeUpdated(this);
                ArrayList values = new ArrayList();
                data_type[] datatypes = db_structure.GetDataTypes(this);
                bool[] nullable = db_structure.GetIsNullable(this);
                bool[] writable = db_structure.GetIsWritable(this);
                object outv, v;

                // try type casting the properties
                for (int i = 0; i < ps.Length; i++)
                {
                    if (!writable[i]) continue; //ignore readonly fields

                    v = ps[i].GetValue(this, null);
                    if (v == null && !nullable[i])
                    {
                        throw new Exception(
                            "tried to insert null value in non-nullable field; insert/update fails");
                        //return null;
                    }

                    //try the specific handler first
                    if (handler[i] != null && handler[i].Invoke(this, v, out outv, true) == 0)
                    {
                        values.Add(outv.ToString());
                        continue;
                    }

                    //otherwise try generalized method
                    if (convert_to_db_datatype(datatypes[i], v, out outv) != 0)
                    {
                        //conversion fails
                        throw new Exception(string.Format(
                            "Data type conversion from {1} failed", ps[i].PropertyType.ToString()));
                        break;
                    }

                    values.Add((outv == null) ? "null" : outv.ToString());
                }

                return values;
            }
            catch (Exception e)
            {
                throw e;
                Debug.HandleException(e);
                return null;
            }

        }


        internal static bool is_same(data_type target_type, object v1, object v2)
        {
            if (v1 == null && v2 == null) return true;
            else if (v1 == null || v2 == null) return false;

            switch (target_type)
            {
                default:
                case data_type.text:
                    return v1.ToString() == v2.ToString();
                case data_type.number:
                    try
                    {
                        int i1 = Convert.ToInt32(v1.GetType().IsSubclassOf(typeof(System.Enum)) ? v1 : v1.ToString());
                        int i2 = Convert.ToInt32(v2.GetType().IsSubclassOf(typeof(System.Enum)) ? v2 : v2.ToString());
                        return i1 == i2;
                    }
                    catch (Exception e)
                    {
                        throw new Exception(string.Format("Cannot compare {0} and {1} as number",
                            v1.ToString(),v2.ToString()), e);
                    }
                case data_type.boolean:
                    if (v1 is bool && v2 is bool)
                        return (bool)v1 == (bool)v2;
                    else
                        return is_same(data_type.number, v1, v2);
                case data_type.number_array:
                    if (v1 is int[] && v2 is int[])
                    {
                        if (((int[])v1).Length != ((int[])v2).Length)
                            return false;

                        for (int i = 0; i < ((int[])v1).Length; i++)
                            if (((int[])v1) [i] != ((int[])v2)[i]) return false;

                        return true;
                    }
                    else
                        throw new Exception(string.Format("Cannot compare {0} and {1} as number array!",
                            v1.ToString(),v2.ToString()));
            }
        }
        internal static int convert_to_db_datatype(
            data_type target_type, object original_value, out object out_value)
        {
            out_value = null;
            if (original_value == null)  return 0; 

            switch (target_type)
            {
                default:
                case data_type.text:
                    out_value = Database.Quote(original_value.ToString());
                    return 0;
                case data_type.number:
                    try
                    {
                        if (original_value.GetType().IsSubclassOf(typeof(System.Enum)))
                        {
                            out_value = Convert.ToInt32(original_value);
                        }
                        else
                        {
                            out_value = Convert.ToInt32(original_value.ToString());
                        }
                        if ((int)out_value ==0)
                            throw new Exception("debug only... you sure 0 here is fine?");
                        return 0;
                    }
                    catch (Exception e)
                    {
                        throw e;
                        Debug.HandleException(new Exception(string.Format(
                            "Cannot convert {0} from string to number",
                            original_value.ToString()), e));
                        return 1;
                    }
                case data_type.boolean:
                    if (original_value is bool)
                    {
                        out_value = ((bool)original_value) ? 1 : 0;
                        return 0;
                    }
                    else
                    {
                        object num;
                        int result = convert_to_db_datatype(data_type.number,
                            original_value, out num);
                        if (result == 0)
                        {
                            out_value = ((int)num == 0) ? 0 : 1;
                        }
                        return result;
                    }
                case data_type.number_array:
                    if (original_value is int[])
                    {
                        int[] nums = (int[])original_value;
                        if (nums.Length == 0) return 0;

                        StringBuilder s = new StringBuilder();
                        for (int i = 0; i < nums.Length; i++)
                        {
                            s.Append(nums[i].ToString() + ",");
                        }
                        out_value = Database.Quote(s.ToString());
                        return 0;
                    }
                    else
                    {
                        return 1;
                    }
            }
        }
        internal static int convert_from_db_datatype(
            data_type target_type, object original_value, out object out_value)
        {
            out_value = null;
            if (original_value == null) { return 0; }

            switch (target_type)
            {
                default:
                case data_type.text:
                    out_value = original_value.ToString();
                    return 0;
                case data_type.number:
                    try
                    {
                        if (original_value.ToString() == "")
                            return 0;
                        out_value = Convert.ToInt32(original_value.ToString());
                        return 0;
                    }
                    catch (Exception e)
                    {
                        throw e; 
                        Debug.HandleException(new Exception(string.Format(
                            "Cannot convert {0} from string to number",
                            original_value.ToString()), e));
                        return 1;
                    }
                case data_type.boolean:
                    if (original_value is bool)
                    {
                        out_value = original_value;
                        return 0;
                    }
                    else
                    {
                        object num;
                        int result = convert_from_db_datatype(data_type.number,
                            original_value, out num);
                        if (result == 0)
                        {
                            out_value = ((int)num == 0) ? false : true;
                        }
                        return result;
                    }
                case data_type.number_array:
                    string[] s = ((string)original_value).Split(new string[] { "," },
                                StringSplitOptions.RemoveEmptyEntries);
                    ArrayList nums = new ArrayList();

                    for (int i = 0; i < s.Length; i++)
                    {
                        nums.Add(Convert.ToInt32(s[i]));
                    }
                    out_value = nums.ToArray(typeof(int));
                    return 0;
            }
        }
        internal static int convert_from_db_datatype(
            string target_type, object original_value, out object out_value)
        {
            if (original_value == null) { out_value = null; return 0; }
            data_type type;
            //Console.WriteLine(target_type);
            switch (target_type)
            {
                case "System.String":
                    type = data_type.text;
                    break;
                case "System.Nullable`1[System.Int32]":
                case "libdb.AlbumType":
                case "System.Int32":
                    type = data_type.number;
                    break;
                case "System.Boolean":
                    type = data_type.boolean;
                    break;
                case "System.Int32[]":
                    type = data_type.number_array;
                    break;
                default:
                    Debug.HandleException(new Exception(String.Format(
                        "Don't know how to convert from {0} to {1}",
                        original_value.GetType().Name, target_type)));
                    out_value = null;
                    return 1;
            }

            return convert_from_db_datatype(type, original_value, out out_value);
        }
        
    }


}
