/********************************************************
 * Project Name   : VAdvantage
 * Class Name     : MRfQ
 * Purpose        : RfQ Model
 * Class Used     : X_C_RfQ
 * Chronological    Development
 * Raghunandan     10-Aug.-2009
  ******************************************************/
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VAdvantage.Classes;
using VAdvantage.Common;
using VAdvantage.Process;
using System.Windows.Forms;
using VAdvantage.Model;
using VAdvantage.DataBase;
using VAdvantage.SqlExec;
using VAdvantage.Utility;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using VAdvantage.Logging;
using VAdvantage.Print;

namespace VAdvantage.Model
{
    public class MRfQ : X_C_RfQ, DocAction
    {

        //Cache	
        private static CCache<int, MRfQ> s_cache = new CCache<int, MRfQ>("C_RfQ", 10);
        private String _processMsg = null;
        private bool _justPrepared = false;
        private MRfQLine[] _lines = null;

        /// <summary>
        /// Get MRfQ from Cache
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="C_RfQ_ID">ID</param>
        /// <param name="trxName">transction</param>
        /// <returns>MRFQ</returns>
        public static MRfQ Get(Ctx ctx, int C_RfQ_ID, Trx trxName)
        {
            int key = C_RfQ_ID;
            MRfQ retValue = (MRfQ)s_cache[key];
            if (retValue != null)
            {
                return retValue;
            }
            retValue = new MRfQ(ctx, C_RfQ_ID, trxName);
            if (retValue.Get_ID() != 0)
            {
                s_cache.Add(key, retValue);
            }
            return retValue;
        }

        /// <summary>
        /// Standard Constructor
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="C_RfQ_ID">ID</param>
        /// <param name="trxName">transaction</param>
        public MRfQ(Ctx ctx, int C_RfQ_ID, Trx trxName)
            :base(ctx, C_RfQ_ID, trxName)
        {
            if (C_RfQ_ID == 0)
            {
                //	setC_RfQ_Topic_ID (0);
                //	setName (null);
                //	setC_Currency_ID (0);	// @$C_Currency_ID @
                //	setSalesRep_ID (0);
                //
                SetDateResponse(DateTime.Now);// Commented by Bharat on 15 Jan 2019 for point given by Puneet
                SetDateWorkStart(DateTime.Now);// Convert.ToDateTime(System.currentTimeMillis()));
                SetIsInvitedVendorsOnly(false);
                SetQuoteType(QUOTETYPE_QuoteSelectedLines);
                SetIsQuoteAllQty(false);
                SetIsQuoteTotalAmt(false);
                SetIsRfQResponseAccepted(true);
                SetIsSelfService(true);
                SetProcessed(false);
            }
        }

        /// <summary>
        /// Load Constructor
        /// </summary>
        /// <param name="ctx">Ctx</param>
        /// <param name="dr">dataroe</param>
        /// <param name="trxName">transaction</param>
        public MRfQ(Ctx ctx, DataRow dr, Trx trxName)
            : base(ctx, dr, trxName)
        {

        }

        /// <summary>
        /// Get active Lines
        /// </summary>
        /// <returns>array of lines</returns>
        public MRfQLine[] GetLines()
        {
            List<MRfQLine> list = new List<MRfQLine>();
            String sql = "SELECT * FROM C_RfQLine "
                + "WHERE C_RfQ_ID=@param1 AND IsActive='Y' "
                + "ORDER BY Line";
            DataTable dt = null;
            IDataReader idr = null;
            SqlParameter[] param = null;
            try
            {
                param = new SqlParameter[1];
                param[0] = new SqlParameter("@param1", GetC_RfQ_ID());
                idr = DB.ExecuteReader(sql, param, Get_TrxName());
                dt = new DataTable();
                dt.Load(idr);
                idr.Close();
                foreach (DataRow dr in dt.Rows)// while (dr.next())
                {
                    list.Add(new MRfQLine(GetCtx(), dr, Get_TrxName()));
                }
            }
            catch (Exception e)
            {
                log.Log(VAdvantage.Logging.Level.SEVERE, sql, e);
            }
            finally
            {
                dt = null;
                idr.Close();
            }

            MRfQLine[] retValue = new MRfQLine[list.Count];
            retValue = list.ToArray();
            return retValue;
        }

        /// <summary>
        /// Get RfQ Responses
        /// </summary>
        /// <param name="activeOnly">active responses only</param>
        /// <param name="completedOnly">complete responses only</param>
        /// <returns>array of lines</returns>
        public MRfQResponse[] GetResponses(bool activeOnly, bool completedOnly)
        {
            List<MRfQResponse> list = new List<MRfQResponse>();
            String sql = "SELECT * FROM C_RfQResponse "
                + "WHERE C_RfQ_ID=@param1";
            if (activeOnly)
            {
                sql += " AND IsActive='Y'";
            }
            if (completedOnly)
            {
                sql += " AND IsComplete='Y'";
            }
            sql += " ORDER BY Price";
            DataTable dt = null;
            IDataReader idr = null;
            SqlParameter[] param = null;
            try
            {
                param = new SqlParameter[1];
                param[0] = new SqlParameter("@param1", GetC_RfQ_ID());
                idr = DataBase.DB.ExecuteReader(sql, param, Get_TrxName());
                dt = new DataTable();
                dt.Load(idr);
                idr.Close();
                foreach (DataRow dr in dt.Rows)// while (dr.next())
                {
                    list.Add(new MRfQResponse(GetCtx(), dr, Get_TrxName()));
                }
            }
            catch
            {
                //log.log(Level.SEVERE, sql, e);
            }
            finally
            {
                dt = null;
                idr.Close();
            }

            MRfQResponse[] retValue = new MRfQResponse[list.Count];
            retValue = list.ToArray();
            return retValue;
        }

        /// <summary>
        /// String Representation
        /// </summary>
        /// <returns>info</returns>
        public override String ToString()
        {
            StringBuilder sb = new StringBuilder("MRfQ[");
            sb.Append(Get_ID()).Append(",Name=").Append(GetName())
                .Append(",QuoteType=").Append(GetQuoteType())
                .Append("]");
            return sb.ToString();
        }

        /// <summary>
        /// Is Quote Total Amt Only
        /// </summary>
        /// <returns>true if total amout only</returns>
        public bool IsQuoteTotalAmtOnly()
        {
            return QUOTETYPE_QuoteTotalOnly.Equals(GetQuoteType());
        }

        /// <summary>
        /// Is Quote Selected Lines
        /// </summary>
        /// <returns>true if quote selected lines</returns>
        public bool IsQuoteSelectedLines()
        {
            return QUOTETYPE_QuoteSelectedLines.Equals(GetQuoteType());
        }

        /// <summary>
        /// Is Quote All Lines
        /// </summary>
        /// <returns>true if quote selected lines</returns>
        public bool IsQuoteAllLines()
        {
            return QUOTETYPE_QuoteAllLines.Equals(GetQuoteType());
        }

        /// <summary>
        /// Is "Quote Total Amt Only" Valid
        /// </summary>
        /// <returns>null or error message</returns>
        public String CheckQuoteTotalAmtOnly()
        {
            if (!IsQuoteTotalAmtOnly())
            {
                return null;
            }
            //	Need to check Line Qty
            MRfQLine[] lines = GetLines();
            for (int i = 0; i < lines.Length; i++)
            {
                MRfQLine line = lines[i];
                MRfQLineQty[] qtys = line.GetQtys();
                if (qtys.Length > 1)
                {
                    log.Warning("isQuoteTotalAmtOnlyValid - #" + qtys.Length + " - " + line);

                    String msg = "@Line@ " + line.GetLine()
                        + ": #@C_RfQLineQty@=" + qtys.Length + " - @IsQuoteTotalAmt@";
                    return msg;
                }
            }
            return null;
        }

        /// <summary>
        /// Before Save
        /// </summary>
        /// <param name="newRecord">newRecord new</param>
        /// <returns>true</returns>
        protected override bool BeforeSave(bool newRecord)
        {
            //	Calculate Complete Date (also used to verify)
            if (GetDateWorkStart() != null && GetDeliveryDays() != 0)
            {
                SetDateWorkComplete(TimeUtil.AddDays(GetDateWorkStart(), GetDeliveryDays()));
            }
            //	Calculate Delivery Days
            else if (GetDateWorkStart() != null && GetDeliveryDays() == 0 && GetDateWorkComplete() != null)
            {
                SetDeliveryDays(TimeUtil.GetDaysBetween(GetDateWorkStart(), GetDateWorkComplete()));
            }
            //	Calculate Start Date
            else if (GetDateWorkStart() == null && GetDeliveryDays() != 0 && GetDateWorkComplete() != null)
            {
                SetDateWorkStart(TimeUtil.AddDays(GetDateWorkComplete(), GetDeliveryDays() * -1));
            }
            return true;
        }

        //Added by Vivek 23-12-2015
        public new void SetProcessed(bool processed)
        {
            base.SetProcessed(processed);
            //if (Get_ID() == 0)
            //    return;
            String setline = "SET Processed='"
                + (processed ? "Y" : "N")
                + "' WHERE C_Rfq_ID =" + GetC_RfQ_ID();
            int noLine = DataBase.DB.ExecuteQuery("UPDATE C_RfQLine " + setline, null, Get_Trx());

            string setQty = @"UPDATE C_RFQLINEQTY SET PROCESSED ='Y' WHERE C_RFQLINEQTY_ID IN (SELECT LQTY.C_RFQLINEQTY_ID FROM C_RFQLINEQTY LQTY INNER JOIN C_RFQLINE LINE
                    ON LINE.C_RFQLINE_ID=LQTY.C_RFQLINE_ID INNER JOIN C_RFQ RFQ ON RFQ.C_RFQ_ID = LINE.C_RFQ_ID WHERE RFQ.C_RFQ_ID =" + GetC_RfQ_ID() + ")";
            int noQty = DataBase.DB.ExecuteQuery(setQty, null, Get_Trx());

            log.Fine(processed + " - Lines=" + noLine + ", Qty=" + noQty);
        }

        /// <summary>
        /// Process document
        /// </summary>
        /// <param name="processAction">document action</param>
        /// <returns>true if performed</returns>
        public bool ProcessIt(String processAction)
        {
            _processMsg = null;
            DocumentEngine engine = new DocumentEngine(this, GetDocStatus());
            return engine.ProcessIt(processAction, GetDocAction());
        }

        /// <summary>
        /// Unlock Document.
        /// </summary>
        /// <returns>true if success</returns>
        public bool UnlockIt()
        {
            log.Info("unlockIt - " + ToString());
            SetProcessing(false);
            return true;
        }

        /// <summary>
        /// Invalidate Document
        /// </summary>
        /// <returns>true if success</returns>
        public bool InvalidateIt()
        {
            log.Info(ToString());
            SetDocAction(DOCACTION_Prepare);
            return true;
        }

        /// <summary>
        /// Prepare Document
        /// </summary>
        /// <returns>new status (In Progress or Invalid)</returns>
        public string PrepareIt()
        {
            log.Info(ToString());
            _processMsg = ModelValidationEngine.Get().FireDocValidate(this, ModalValidatorVariables.DOCTIMING_BEFORE_PREPARE);
            if (_processMsg != null)
                return DocActionVariables.STATUS_INVALID;

            MDocType dt = MDocType.Get(GetCtx(), GetC_DocType_ID());
            //	Std Period open?
            if (!MPeriod.IsOpen(GetCtx(), GetDateResponse(), dt.GetDocBaseType(), GetAD_Org_ID()))
            {
                _processMsg = "@PeriodClosed@";
                return DocActionVariables.STATUS_INVALID;
            }

            // is Non Business Day?            
            if (MNonBusinessDay.IsNonBusinessDay(GetCtx(), GetDateResponse(), GetAD_Org_ID()))
            {
                _processMsg = Common.Common.NONBUSINESSDAY;
                return DocActionVariables.STATUS_INVALID;
            }

            //	Lines
            MRfQLine[] lines = GetLines();
            if (lines.Length == 0)
            {
                _processMsg = "@NoLines@";
                return DocActionVariables.STATUS_INVALID;
            }

            _justPrepared = true;
            return DocActionVariables.STATUS_INPROGRESS;
        }


        /// <summary>
        /// Approve Document
        /// </summary>
        /// <returns>true if success</returns>
        public bool ApproveIt()
        {
            log.Info("approveIt - " + ToString());
            //SetIsApproved(true);
            return true;
        }

        /// <summary>
        /// Reject Approval
        /// </summary>
        /// <returns>true if success</returns>
        public bool RejectIt()
        {
            log.Info("rejectIt - " + ToString());
            //SetIsApproved(false);
            return true;
        }

        /// <summary>
        /// Complete Document
        /// </summary>
        /// <returns>new status (Complete, In Progress, Invalid, Waiting ..)</returns>
        /****************************************************************************************************/
        public string CompleteIt()
        {
            log.Info(ToString());
            StringBuilder Info = new StringBuilder();
            SetProcessed(true);

            //User Validation
            String valid = ModelValidationEngine.Get().FireDocValidate(this, ModalValidatorVariables.DOCTIMING_AFTER_COMPLETE);
            if (valid != null)
            {
                if (Info.Length > 0)
                    Info.Append(" - ");
                Info.Append(valid);
                _processMsg = Info.ToString();
                return DocActionVariables.STATUS_INVALID;
            }

            _processMsg = Info.ToString();
            SetDocAction(DOCACTION_Close);
            return DocActionVariables.STATUS_COMPLETED;
        }

        /// <summary>
        /// Void Document.
        ///	Set Qtys to 0 - Sales: reverse all documents
        /// </summary>
        /// <returns>true if success</returns>
        public bool VoidIt()
        {
            log.Info(ToString());
            SetProcessed(true);
            SetDocAction(DOCACTION_None);
            return true;
        }

        /// <summary>
        /// Close Document. Cancel not delivered Quantities
        /// </summary>
        /// <returns>true if success</returns>
        public bool CloseIt()
        {
            log.Info(ToString());
            SetProcessed(true);
            SetDocAction(DOCACTION_None);
            return true;
        }

        /// <summary>
        /// Reverse Correction - same void
        /// </summary>
        /// <returns>true if success</returns>
        public bool ReverseCorrectIt()
        {
            log.Info(ToString());
            return VoidIt();
        }

        /// <summary>
        /// Reverse Accrual - none
        /// </summary>
        /// <returns>false</returns>
        public bool ReverseAccrualIt()
        {
            log.Info(ToString());
            return false;
        }

        /// <summary>
        /// Re-activate.
        /// </summary>
        /// <returns>true if success</returns>
        public bool ReActivateIt()
        {
            log.Info(ToString());
            int no = 1;
            //VIS0336:changes done for deleting the Responses when RFQ is reactivated
            StringBuilder Sql = new StringBuilder();
            Sql.Append("DELETE FROM C_RfQResponseLineQty WHERE C_RfQResponseLineQty_ID IN (SELECT C_RfQResponseLineQty_ID FROM C_RfQResponseLineQty WHERE C_RfQResponseLine_ID IN( SELECT C_RfQResponseLine_ID FROM " +
                " C_RfQResponseLine WHERE C_RfQResponse_ID IN (SELECT C_RfQResponse_ID FROM C_RfQResponse WHERE VAS_Response_ID=(SELECT VAS_Response_ID " +
                " FROM VAS_Response WHERE C_RfQ_ID=" + GetC_RfQ_ID() + "))))");
            no = DB.ExecuteQuery(Sql.ToString(), null, Get_Trx());
            if (no < 0)
            {
                _processMsg = Msg.GetMsg(GetCtx(), "VAS_ReslineQtyNotDeleted");
                return false;
            }
            Sql.Clear();
            Sql.Append("DELETE FROM C_RfQResponseLine WHERE C_RfQResponseLine_ID IN (SELECT C_RfQResponseLine_ID FROM C_RfQResponseLine WHERE C_RfQResponse_ID IN (SELECT C_RfQResponse_ID FROM C_RfQResponse WHERE " +
                " VAS_Response_ID=(SELECT VAS_Response_ID FROM VAS_Response WHERE C_RfQ_ID=" + GetC_RfQ_ID() + ")))");
            no = DB.ExecuteQuery(Sql.ToString(), null, Get_Trx());
            if (no < 0)
            {
                _processMsg = Msg.GetMsg(GetCtx(), "VAS_ReslineNotDeleted");
                return false;
            }
            Sql.Clear();
            Sql.Append("DELETE FROM C_RfQResponse WHERE VAS_Response_ID =(SELECT VAS_Response_ID FROM VAS_Response WHERE C_RfQ_ID=" + GetC_RfQ_ID() + ")");
            no = DB.ExecuteQuery(Sql.ToString(), null, Get_Trx());
            if (no < 0)
            {
                _processMsg = Msg.GetMsg(GetCtx(), "VAS_ResNotDeleted");
                return false;
            }

            SetDocAction(DOCACTION_Complete);
            SetProcessed(false);
            return true;
        }

        /// <summary>
        /// Get Summary
        /// </summary>
        /// <returns>Summary of Document</returns>
        public String GetSummary()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(GetDocumentNo());
            sb.Append(": ").
                Append(Msg.Translate(GetCtx(), "Total Amount")).Append("=").Append(GetTotalAmt());
            //	 - Description
            if (GetDescription() != null && GetDescription().Length > 0)
                sb.Append(" - ").Append(GetDescription());
            return sb.ToString();
        }

        /// <summary>
        /// Get Process Message
        /// </summary>
        /// <returns>clear text error message</returns>
        public String GetProcessMsg()
        {
            return _processMsg;
        }
        /// <summary>
        /// Get Document Owner (Responsible)
        /// </summary>
        /// <returns>AD_User_ID</returns>
        public int GetDoc_User_ID()
        {
            return GetSalesRep_ID();
        }

        /// <summary>
        /// Get Document Approval Amount
        /// </summary>
        /// <returns>amount</returns>
        public Decimal GetApprovalAmt()
        {
            return GetTotalAmt();
        }

        /// <summary>
        /// Get Document Info
        /// </summary>
        /// <returns>document Info (untranslated)</returns>
        public String GetDocumentInfo()
        {
            MDocType dt = MDocType.Get(GetCtx(), GetC_DocType_ID());
            return dt.GetName() + " " + GetDocumentNo();
        }

        /// <summary>
        /// Create PDF
        /// </summary>
        /// <returns>File or null</returns>
        public FileInfo CreatePDF()
        {
            try
            {
                String fileName = Get_TableName() + Get_ID() + "_" + CommonFunctions.GenerateRandomNo() + ".pdf";
                string filePath = Path.Combine(GlobalVariable.PhysicalPath, "TempDownload", fileName);


                ReportEngine_N re = ReportEngine_N.Get(GetCtx(), ReportEngine_N.ORDER, GetC_Order_ID());
                if (re == null)
                    return null;

                re.GetView();
                bool b = re.CreatePDF(filePath);

                FileInfo temp = new FileInfo(filePath);
                if (!temp.Exists)
                {
                    b = re.CreatePDF(filePath);
                    if (b)
                    {
                        return new FileInfo(filePath);
                    }
                    return null;
                }
                else
                    return temp;
            }
            catch (Exception e)
            {
                log.Severe("Could not create PDF - " + e.Message);
            }
            return null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        public FileInfo CreatePDF(FileInfo file)
        {
            return null;
        }

        public Env.QueryParams GetLineOrgsQueryInfo()
        {
            return null;
        }

        public DateTime? GetDocumentDate()
        {
            return null;
        }

        public string GetDocBaseType()
        {
            return null;
        }

        public void SetProcessMsg(string processMsg)
        {
            _processMsg = processMsg;
        }
    }
}
