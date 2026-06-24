namespace ModelLibrary.Model{
/** Generated Model - DO NOT CHANGE */
using System;using System.Text;using VAdvantage.DataBase;using VAdvantage.Common;using VAdvantage.Classes;using VAdvantage.Process;using VAdvantage.Model;using VAdvantage.Utility;using System.Data;/** Generated Model for VAS_OppLines
 *  @author Raghu (Updated) 
 *  @version Vienna Framework 1.1.1 - $Id$ */
public class X_VAS_OppLines : PO{public X_VAS_OppLines (Context ctx, int VAS_OppLines_ID, Trx trxName) : base (ctx, VAS_OppLines_ID, trxName){/** if (VAS_OppLines_ID == 0){SetVAS_OppLines_ID (0);} */
}public X_VAS_OppLines (Ctx ctx, int VAS_OppLines_ID, Trx trxName) : base (ctx, VAS_OppLines_ID, trxName){/** if (VAS_OppLines_ID == 0){SetVAS_OppLines_ID (0);} */
}/** Load Constructor 
@param ctx context
@param rs result set 
@param trxName transaction
*/
public X_VAS_OppLines (Context ctx, DataRow rs, Trx trxName) : base(ctx, rs, trxName){}/** Load Constructor 
@param ctx context
@param rs result set 
@param trxName transaction
*/
public X_VAS_OppLines (Ctx ctx, DataRow rs, Trx trxName) : base(ctx, rs, trxName){}/** Load Constructor 
@param ctx context
@param rs result set 
@param trxName transaction
*/
public X_VAS_OppLines (Ctx ctx, IDataReader dr, Trx trxName) : base(ctx, dr, trxName){}/** Static Constructor 
 Set Table ID By Table Name
 added by ->Harwinder */
static X_VAS_OppLines(){ Table_ID = Get_Table_ID(Table_Name); model = new KeyNamePair(Table_ID,Table_Name);}/** Serial Version No */
static long serialVersionUID = 28062534297801L;/** Last Updated Timestamp 6/2/2026 7:33:01 PM */
public static long updatedMS = 1780408981012L;/** AD_Table_ID=1000724 */
public static int Table_ID; // =1000724;
/** TableName=VAS_OppLines */
public static String Table_Name="VAS_OppLines";
protected static KeyNamePair model;protected Decimal accessLevel = new Decimal(3);/** AccessLevel
@return 3 - Client - Org 
*/
protected override int Get_AccessLevel(){return Convert.ToInt32(accessLevel.ToString());}/** Load Meta Data
@param ctx context
@return PO Info
*/
protected override POInfo InitPO (Context ctx){POInfo poi = POInfo.GetPOInfo (ctx, Table_ID);return poi;}/** Load Meta Data
@param ctx context
@return PO Info
*/
protected override POInfo InitPO (Ctx ctx){POInfo poi = POInfo.GetPOInfo (ctx, Table_ID);return poi;}/** Info
@return info
*/
public override String ToString(){StringBuilder sb = new StringBuilder ("X_VAS_OppLines[").Append(Get_ID()).Append("]");return sb.ToString();}/** Set Forecast Quantity.
@param BaseQuantity Here User enters the Forecast Quantity of the Product Selected */
public void SetBaseQuantity (Decimal? BaseQuantity){Set_Value ("BaseQuantity", (Decimal?)BaseQuantity);}/** Get Forecast Quantity.
@return Here User enters the Forecast Quantity of the Product Selected */
public Decimal GetBaseQuantity() {Object bd =Get_Value("BaseQuantity");if (bd == null) return Env.ZERO;return  Convert.ToDecimal(bd);}/** Set Charge.
@param C_Charge_ID Additional document charges */
public void SetC_Charge_ID (int C_Charge_ID){if (C_Charge_ID <= 0) Set_Value ("C_Charge_ID", null);else
Set_Value ("C_Charge_ID", C_Charge_ID);}/** Get Charge.
@return Additional document charges */
public int GetC_Charge_ID() {Object ii = Get_Value("C_Charge_ID");if (ii == null) return 0;return Convert.ToInt32(ii);}/** Set UOM.
@param C_UOM_ID Unit of Measure */
public void SetC_UOM_ID (int C_UOM_ID){if (C_UOM_ID <= 0) Set_Value ("C_UOM_ID", null);else
Set_Value ("C_UOM_ID", C_UOM_ID);}/** Get UOM.
@return Unit of Measure */
public int GetC_UOM_ID() {Object ii = Get_Value("C_UOM_ID");if (ii == null) return 0;return Convert.ToInt32(ii);}/** Set Description.
@param Description Optional short description of the record */
public void SetDescription (String Description){if (Description != null && Description.Length > 255){log.Warning("Length > 255 - truncated");Description = Description.Substring(0,255);}Set_Value ("Description", Description);}/** Get Description.
@return Optional short description of the record */
public String GetDescription() {return (String)Get_Value("Description");}/** Set Discount %.
@param Discount Discount in percent */
public void SetDiscount (Decimal? Discount){Set_Value ("Discount", (Decimal?)Discount);}/** Get Discount %.
@return Discount in percent */
public Decimal GetDiscount() {Object bd =Get_Value("Discount");if (bd == null) return Env.ZERO;return  Convert.ToDecimal(bd);}/** Set Export.
@param Export_ID Export */
public void SetExport_ID (String Export_ID){if (Export_ID != null && Export_ID.Length > 50){log.Warning("Length > 50 - truncated");Export_ID = Export_ID.Substring(0,50);}Set_Value ("Export_ID", Export_ID);}/** Get Export.
@return Export */
public String GetExport_ID() {return (String)Get_Value("Export_ID");}/** Set Label.
@param Label Label */
public void SetLabel (Object Label){Set_Value ("Label", Label);}/** Get Label.
@return Label */
public Object GetLabel() {return Get_Value("Label");}/** Set Attribute Set Instance.
@param M_AttributeSetInstance_ID Product Attribute Set Instance */
public void SetM_AttributeSetInstance_ID (int M_AttributeSetInstance_ID){if (M_AttributeSetInstance_ID <= 0) Set_Value ("M_AttributeSetInstance_ID", null);else
Set_Value ("M_AttributeSetInstance_ID", M_AttributeSetInstance_ID);}/** Get Attribute Set Instance.
@return Product Attribute Set Instance */
public int GetM_AttributeSetInstance_ID() {Object ii = Get_Value("M_AttributeSetInstance_ID");if (ii == null) return 0;return Convert.ToInt32(ii);}/** Set Product.
@param M_Product_ID Product, Service, Item */
public void SetM_Product_ID (int M_Product_ID){if (M_Product_ID <= 0) Set_Value ("M_Product_ID", null);else
Set_Value ("M_Product_ID", M_Product_ID);}/** Get Product.
@return Product, Service, Item */
public int GetM_Product_ID() {Object ii = Get_Value("M_Product_ID");if (ii == null) return 0;return Convert.ToInt32(ii);}/** Set Total Amount.
@param PlannedAmt Total Amount */
public void SetPlannedAmt (Decimal? PlannedAmt){Set_Value ("PlannedAmt", (Decimal?)PlannedAmt);}/** Get Total Amount.
@return Total Amount */
public Decimal GetPlannedAmt() {Object bd =Get_Value("PlannedAmt");if (bd == null) return Env.ZERO;return  Convert.ToDecimal(bd);}/** Set Planned Margin.
@param PlannedMarginAmt Opportunity planned margin amount */
public void SetPlannedMarginAmt (Decimal? PlannedMarginAmt){Set_Value ("PlannedMarginAmt", (Decimal?)PlannedMarginAmt);}/** Get Planned Margin.
@return Opportunity planned margin amount */
public Decimal GetPlannedMarginAmt() {Object bd =Get_Value("PlannedMarginAmt");if (bd == null) return Env.ZERO;return  Convert.ToDecimal(bd);}/** Set Planned Price.
@param PlannedPrice Planned price for this opportunity line */
public void SetPlannedPrice (Decimal? PlannedPrice){Set_Value ("PlannedPrice", (Decimal?)PlannedPrice);}/** Get Planned Price.
@return Planned price for this opportunity line */
public Decimal GetPlannedPrice() {Object bd =Get_Value("PlannedPrice");if (bd == null) return Env.ZERO;return  Convert.ToDecimal(bd);}/** Set Planned Quantity.
@param PlannedQty Planned quantity for this project */
public void SetPlannedQty (Decimal? PlannedQty){Set_Value ("PlannedQty", (Decimal?)PlannedQty);}/** Get Planned Quantity.
@return Planned quantity for this project */
public Decimal GetPlannedQty() {Object bd =Get_Value("PlannedQty");if (bd == null) return Env.ZERO;return  Convert.ToDecimal(bd);}/** Set List Price.
@param PriceList List Price */
public void SetPriceList (Decimal? PriceList){Set_Value ("PriceList", (Decimal?)PriceList);}/** Get List Price.
@return List Price */
public Decimal GetPriceList() {Object bd =Get_Value("PriceList");if (bd == null) return Env.ZERO;return  Convert.ToDecimal(bd);}/** Set Line No..
@param VAS_LineNo Line No. */
public void SetVAS_LineNo (int VAS_LineNo){Set_Value ("VAS_LineNo", VAS_LineNo);}/** Get Line No..
@return Line No. */
public int GetVAS_LineNo() {Object ii = Get_Value("VAS_LineNo");if (ii == null) return 0;return Convert.ToInt32(ii);}/** Set VAS_OppLines_ID.
@param VAS_OppLines_ID VAS_OppLines_ID */
public void SetVAS_OppLines_ID (int VAS_OppLines_ID){if (VAS_OppLines_ID < 1) throw new ArgumentException ("VAS_OppLines_ID is mandatory.");Set_ValueNoCheck ("VAS_OppLines_ID", VAS_OppLines_ID);}/** Get VAS_OppLines_ID.
@return VAS_OppLines_ID */
public int GetVAS_OppLines_ID() {Object ii = Get_Value("VAS_OppLines_ID");if (ii == null) return 0;return Convert.ToInt32(ii);}/** Set VAS_Opportunity_ID.
@param VAS_Opportunity_ID VAS_Opportunity_ID */
public void SetVAS_Opportunity_ID (int VAS_Opportunity_ID){if (VAS_Opportunity_ID <= 0) Set_Value ("VAS_Opportunity_ID", null);else
Set_Value ("VAS_Opportunity_ID", VAS_Opportunity_ID);}/** Get VAS_Opportunity_ID.
@return VAS_Opportunity_ID */
public int GetVAS_Opportunity_ID() {Object ii = Get_Value("VAS_Opportunity_ID");if (ii == null) return 0;return Convert.ToInt32(ii);}}
}