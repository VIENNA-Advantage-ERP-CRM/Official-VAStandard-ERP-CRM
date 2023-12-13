using System;
using System.Collections.Generic;
//using System.Linq;
using System.Text;
using System.Data;
using VAdvantage.Classes;
using VAdvantage.DataBase;
using VAdvantage.Logging;
using VAdvantage.Utility;
using System.Web;

namespace VAdvantage.Model
{
    public class MLocator : X_M_Locator
    {
        //	Logger						
        private static VLogger _log = VLogger.GetVLogger(typeof(MLocator).FullName);
        //	Cache
        private static CCache<int, MLocator> cache;

        Tuple<String, String, String> mInfo = null;

        /// <summary>
        /// Standard Locator Constructor
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="M_Locator_ID">id</param>
        /// <param name="trxName">transaction</param>
        public MLocator(Ctx ctx, int M_Locator_ID, Trx trxName)
            : base(ctx, M_Locator_ID, trxName)
        {
            if (M_Locator_ID == 0)
            {
                //SetM_Locator_ID(0);		//	PK
                //SetM_Warehouse_ID(0);		//	Parent
                SetIsDefault(false);
                SetPriorityNo(50);
                //SetValue(null);
                //SetX(null);
                //SetY(null);
                //SetZ(null);
            }
        }

        /// <summary>
        /// Load Constructor
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="rs">result set</param>
        /// <param name="trxName">transaction</param>
        public MLocator(Ctx ctx, DataRow rs, Trx trxName)
            : base(ctx, rs, trxName)
        {
        }

        public MLocator(Ctx ctx, IDataReader dr, Trx trxName)
            : base(ctx, dr, trxName)
        {
        }
        /// <summary>
        /// New Locator Constructor with XYZ=000
        /// </summary>
        /// <param name="warehouse">parent</param>
        /// <param name="value">value</param>
        public MLocator(MWarehouse warehouse, String value)
            : this(warehouse.GetCtx(), 0, warehouse.Get_TrxName())
        {
            SetClientOrg(warehouse);
            SetM_Warehouse_ID(warehouse.GetM_Warehouse_ID());		//	Parent
            SetValue(value);
            // Added by Mohit VAWMS 20-8-2015
            if (Env.HasModulePrefix("VAWMS_", out mInfo))
            {
                SetXYZ("0", "0", "0", "0", "0");
            }
            else
            {
                SetXYZ("0", "0", "0");
            }
            //End
        }

        /// <summary>
        /// Set Location
        /// </summary>
        /// <param name="X">x</param>
        /// <param name="Y">y</param>
        /// <param name="Z">z</param>
        public void SetXYZ(String X, String Y, String Z)
        {
            SetX(X);
            SetY(Y);
            SetZ(Z);
        }

        /// <summary>
        /// Get Warehouse Name
        /// </summary>
        /// <returns>name</returns>
        public String GetWarehouseName()
        {
            MWarehouse wh = MWarehouse.Get(GetCtx(), GetM_Warehouse_ID());
            if (wh.Get_ID() == 0)
                return "<" + GetM_Warehouse_ID() + ">";
            return wh.GetName();
        }

        /// <summary>
        /// Get Locator from Cache
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="M_Locator_ID">id</param>
        /// <returns>MLocator</returns>
        public static MLocator Get(Ctx ctx, int M_Locator_ID)
        {
            if (cache == null)
                cache = new CCache<int, MLocator>("M_Locator", 20);
            int key = M_Locator_ID;
            MLocator retValue = null;
            if (cache.ContainsKey(key))
            {
                retValue = (MLocator)cache[key];
            }
            if (retValue != null)
                return retValue;
            retValue = new MLocator(ctx, M_Locator_ID, null);
            if (retValue.Get_ID() != 0)
                cache.Add(key, retValue);
            return retValue;
        }

        /// <summary>
        /// Get the Locator with the combination or create new one
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="M_Warehouse_ID">id</param>
        /// <param name="value">value</param>
        /// <param name="X">x</param>
        /// <param name="Y">y</param>
        /// <param name="Z">z</param>
        /// <returns>locator</returns>
        public static MLocator Get(Ctx ctx, int M_Warehouse_ID, String value,
            String X, String Y, String Z)
        {
            MLocator retValue = null;
            String sql = "SELECT * FROM M_Locator WHERE M_Warehouse_ID=" + M_Warehouse_ID + " AND " +
                "X='" + X + "' AND Y='" + Y + "' AND Z='" + Z + "'";
            DataSet ds = null;
            try
            {
                ds = DataBase.DB.ExecuteDataset(sql, null, null);
                if (ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    DataRow rs = ds.Tables[0].Rows[0];
                    retValue = new MLocator(ctx, rs, null);
                }
            }
            catch (Exception ex)
            {
                _log.Log(Level.SEVERE, "get", ex);
            }

            //
            if (retValue == null)
            {

                MWarehouse wh = MWarehouse.Get(ctx, M_Warehouse_ID);
                retValue = new MLocator(wh, HttpUtility.HtmlEncode(value));
                retValue.SetXYZ(HttpUtility.HtmlEncode(X), HttpUtility.HtmlEncode(Y), HttpUtility.HtmlEncode(Z));
                if (!retValue.Save())
                    retValue = null;
            }
            return retValue;
        }

        /// <summary>
        /// Get oldest Default Locator of warehouse with locator
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="M_Locator_ID">locator</param>
        /// <returns>locator or null</returns>
        public static MLocator GetDefault(Ctx ctx, int M_Locator_ID)
        {
            Trx trxName = null;
            MLocator retValue = null;
            String sql = "SELECT * FROM M_Locator l "
                + "WHERE IsDefault='Y'"
                + " AND EXISTS (SELECT * FROM M_Locator lx "
                    + "WHERE l.M_Warehouse_ID=lx.M_Warehouse_ID AND lx.M_Locator_ID=" + M_Locator_ID + ") "
                + "ORDER BY Created";
            DataSet ds = null;
            try
            {
                ds = DataBase.DB.ExecuteDataset(sql, null, trxName);
                if (ds.Tables.Count > 0)
                {
                    DataRow rs = null;
                    int totCount = ds.Tables[0].Rows.Count;
                    for (int i = 0; i < totCount; i++)
                    {
                        rs = ds.Tables[0].Rows[i];
                        retValue = new MLocator(ctx, rs, trxName);
                    }
                }
            }
            catch (Exception e)
            {
                _log.Log(Level.SEVERE, sql, e);
            }
            return retValue;
        }

        public override String ToString()
        {
            return GetValue();
        }
        public static MLocator GetDefaultLocatorOfOrg(Ctx ctx, int AD_Org_ID)
        {
            MLocator retValue = null;
            List<int> defaultlocators = new List<int>();
            List<int> locators = new List<int>();
            String sql = "SELECT M_Locator_ID, IsDefault FROM M_Locator WHERE (AD_Org_ID=" + AD_Org_ID + " OR 0=" + AD_Org_ID + ")";
            IDataReader idr = null;
            try
            {
                idr = DB.ExecuteReader(sql);
                while (idr != null && idr.Read())
                {
                    if (Util.GetValueOfString(idr[1]) == "Y")
                    {
                        defaultlocators.Add(Util.GetValueOfInt(idr[0]));
                        break;
                    }
                    else
                    {
                        locators.Add(Util.GetValueOfInt(idr[0]));
                    }
                }
                if (idr != null)
                {
                    idr.Close();
                    idr = null;
                }
                if (defaultlocators.Count > 0)
                {
                    retValue = MLocator.Get(ctx, Util.GetValueOfInt(defaultlocators[0]));
                    return retValue;
                }
                if (locators.Count > 0)
                {
                    retValue = MLocator.Get(ctx, Util.GetValueOfInt(locators[0]));
                    return retValue;
                }

            }
            catch (Exception e)
            {

                _log.Log(Level.SEVERE, sql, e);
            }
            finally
            {
                if (idr != null)
                {
                    idr.Close();
                    idr = null;
                }
            }
            return retValue;
        }

        /// <summary>
        ///	Before Save
        /// </summary>
        /// <param name="newRecord">new</param>
        /// <returns>true if can be saved</returns>
        protected override Boolean BeforeSave(Boolean newRecord)
        {
            //  Check Storage
            if (Is_ValueChanged("IsActive") && IsActive())  // now not active 
            {
                if (checkStock(GetCtx(), Get_ID(), Get_TrxName()))
                {
                    log.SaveError("", Msg.GetMsg(GetCtx(), "LocatorHasStock"));  
                    return false;
                }

            } // storage

            // DevOPs Task ID: 59 - Checks for the duplicate Searchkey
            int count = Util.GetValueOfInt(DB.ExecuteScalar("SELECT COUNT(Value) FROM M_Locator WHERE Value= '" + GetValue() +
                "' AND M_Locator_ID !=" + GetM_Locator_ID() + " AND AD_Client_ID = " + GetAD_Client_ID(), null, Get_TrxName()));
            if (count > 0)
            {
                log.SaveError("", Msg.GetMsg(GetCtx(), "SearchKeyUnique"));
                return false;
            }

            if (newRecord
                    || Is_ValueChanged("X")
                    || Is_ValueChanged("Y")
                    || Is_ValueChanged("Z")
                    || Is_ValueChanged("POSITION")
                    || Is_ValueChanged("Bin"))
            {
                MWarehouse wh = new MWarehouse(GetCtx(), GetM_Warehouse_ID(), Get_TrxName());
                
                StringBuilder combination = new StringBuilder();
                combination.Append(GetX()).Append(wh.GetSeparator());
                combination.Append(GetY()).Append(wh.GetSeparator());
                combination.Append(GetZ());
                if (GetPOSITION() != null && GetPOSITION().Length != 0)
                {
                    combination.Append(wh.GetSeparator()).Append(GetPOSITION());
                }
                if (GetBin() != null && GetBin().Length != 0)
                {
                    combination.Append(wh.GetSeparator()).Append(GetBin());
                }
                log.Fine("Set Locator Combination :" + combination);

                string sql = "SELECT COUNT(M_Locator_ID) FROM M_Locator WHERE M_Locator_ID <> " + GetM_Locator_ID() +
                              " AND M_Warehouse_ID =" + GetM_Warehouse_ID() +
                              " AND UPPER(LocatorCombination) = UPPER('" + combination + "')";
                int ii = Util.GetValueOfInt(DB.ExecuteScalar(sql, null, Get_TrxName()));
                if (ii != 0)
                {
                    log.SaveError("", Msg.GetMsg(GetCtx(), "CombinationNotUnique"));
                    return false;
                }

                SetLocatorCombination(combination.ToString());
            }

            if (newRecord
                    || Is_ValueChanged("IsAvailableToPromise")
                    || Is_ValueChanged("IsAvailableForAllocation"))
            {
                if (IsAvailableForAllocation() && !IsAvailableToPromise())
                {
                    log.SaveError("", Msg.GetMsg(GetCtx(), "InvalidCombination"));
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Check if stock available under this Locator
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="M_Locator_ID">Locator ID</param>
        /// <param name="trx">trx</param>
        /// <returns></returns>
        public static bool checkStock(Ctx ctx, int M_Locator_ID, Trx trx)
        {
            string sql = "SELECT QtyOnHand, QtyOrdered, QtyReserved FROM M_Storage WHERE M_Locator_ID=" + M_Locator_ID;
            decimal OnHand = Env.ZERO;
            decimal Ordered = Env.ZERO;
            decimal Reserved = Env.ZERO;

            DataSet dstmt = new DataSet();
            try
            {
                dstmt = DB.ExecuteDataset(sql, null, trx);
                if (dstmt != null && dstmt.Tables.Count > 0 && dstmt.Tables[0].Rows.Count > 0)
                {
                    OnHand = Util.GetValueOfDecimal(dstmt.Tables[0].Rows[0]["QtyOnHand"]);
                    Ordered = Util.GetValueOfDecimal(dstmt.Tables[0].Rows[0]["QtyOrdered"]);
                    Reserved = Util.GetValueOfDecimal(dstmt.Tables[0].Rows[0]["QtyReserved"]);
                }
            }
            catch (Exception ex)
            {
                _log.Log(Level.SEVERE, sql, ex);
            }
            finally
            {
                if (dstmt != null)
                    dstmt.Dispose();
            }

            if ((OnHand != 0) || (Ordered != 0) || (Reserved != 0))
                return true;
            else
                return false;
        }
        public void SetXYZ(String X, String Y, String Z, string Position, String Bin)
        {
            SetX(X);
            SetY(Y);
            SetZ(Z);
            SetBin(Bin);
            SetPOSITION(Position);
        }
        public static MLocator Get(Ctx ctx, int M_Warehouse_ID, String value, String X, String Y, String Z, String Position, String Bin)
        {
            MLocator retValue = null;
            String sql = "SELECT * FROM M_Locator WHERE M_Warehouse_ID=" + M_Warehouse_ID + " AND " +
                "X='" + X + "' AND Y='" + Y + "' AND Z='" + Z + "'";
            DataSet ds = null;
            try
            {
                ds = DB.ExecuteDataset(sql, null, null);
                if (ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    DataRow idr = ds.Tables[0].Rows[0];
                    retValue = new MLocator(ctx, idr, null);
                }
            }
            catch (Exception ex)
            {
                _log.Log(Level.SEVERE, "get", ex);
            }

            //
            if (retValue == null)
            {
                MWarehouse wh = MWarehouse.Get(ctx, M_Warehouse_ID);
                retValue = new MLocator(wh, value);
                retValue.SetXYZ(X, Y, Z, Position, Bin);
                if (!retValue.Save())
                    retValue = null;
            }
            return retValue;
        }
        public Boolean IsFixed()
        {
            String sql = "SELECT count(*) FROM M_ProductLocator pl " +
                            "WHERE pl.M_Locator_ID = @param1 ";

            int ii = DB.GetSQLValue(Get_TrxName(), sql, GetM_Locator_ID());
            if (ii != 0)
            {
                return true;
            }
            return false;
        }

        protected override bool BeforeDelete()
        {
            string sql = @"SELECT Count(M_Product_ID) FROM M_Storage
                    WHERE M_Locator_ID=" + GetM_Locator_ID() + " AND " +
                    "(QtyOnHand > 0 OR QtyOrdered > 0 OR QtyReserved > 0 OR DTD001_QtyReserved > 0 OR DTD001_SourceReserve > 0)";

            int count = Util.GetValueOfInt(DB.ExecuteScalar(sql, null, Get_TrxName()));
            if (count != 0)
            {
                log.SaveError("", Msg.GetMsg(GetCtx(), "VAS_LocatorError"));
                return false;
            }
            return true;
        }
        //END
    }
}
