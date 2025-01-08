/********************************************************
 * Module Name    : 
 * Purpose        : 
 * Class Used     : X_C_ProjectPhase
 * Chronological Development
 * Veena Pandey     17-June-2009
 ******************************************************/

using System;
using System.Collections.Generic;
//using System.Linq;
using System.Text;
using System.Data;
using VAdvantage.Classes;
using VAdvantage.Utility;
using VAdvantage.Logging;
using VAdvantage.DataBase;
using ViennaAdvantage.Model;
using System.Linq;

namespace VAdvantage.Model
{
    public class MProjectPhase : X_C_ProjectPhase
    {

        int ProjectTemplateWindowId = 0;
        int ProjectWindowId = 0;
        /// <summary>
        /// Standard Constructor
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="C_ProjectPhase_ID">id</param>
        /// <param name="trxName">transaction</param>
        public MProjectPhase(Ctx ctx, int C_ProjectPhase_ID, Trx trxName)
            : base(ctx, C_ProjectPhase_ID, trxName)
        {
            if (C_ProjectPhase_ID == 0)
            {
                //	setC_ProjectPhase_ID (0);	//	PK
                //	setC_Project_ID (0);		//	Parent
                //	setC_Phase_ID (0);			//	FK
                SetCommittedAmt(Env.ZERO);
                SetIsCommitCeiling(false);
                SetIsComplete(false);
                SetSeqNo(0);
                //	setName (null);
                SetPlannedAmt(Env.ZERO);
                SetPlannedQty(Env.ZERO);
                SetQty(Env.ZERO);
            }
        }

        /// <summary>
        /// Load Constructor
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="rs">result set</param>
        /// <param name="trxName">transaction</param>
        public MProjectPhase(Ctx ctx, DataRow rs, Trx trxName)
            : base(ctx, rs, trxName)
        {
        }

        /// <summary>
        /// Parent Constructor
        /// </summary>
        /// <param name="project">parent</param>
        public MProjectPhase(MProject project)
            : this(project.GetCtx(), 0, project.Get_TrxName())
        {
            SetClientOrg(project);
            SetC_Project_ID(project.GetC_Project_ID());
        }

        /// <summary>
        /// Copy Constructor
        /// </summary>
        /// <param name="project">parent</param>
        /// <param name="phase">copy</param>
        public MProjectPhase(MProject project, MProjectTypePhase phase)
            : this(project)
        {


            //

            SetC_Phase_ID(phase.GetC_Phase_ID());			//	FK
            SetName(phase.GetName());
            //SetSeqNo(phase.GetSeqNo());
            SetDescription(phase.GetDescription());
            SetHelp(phase.GetHelp());
            if (phase.GetM_Product_ID() != 0)
                SetM_Product_ID(phase.GetM_Product_ID());
            SetQty(phase.GetStandardQty());
        }

        /// <summary>
        /// Copy Tasks from other Phase
        /// </summary>
        /// <param name="fromPhase">from phase</param>
        /// <returns>number of tasks copied</returns>
        public int CopyTasksFrom(MProjectPhase fromPhase)
        {
            if (fromPhase == null)
                return 0;
            int count = 0;
            //
            MProjectTask[] myTasks = GetTasks();
            MProjectTask[] fromTasks = fromPhase.GetTasks();
            //	Copy Project Tasks
            for (int i = 0; i < fromTasks.Length; i++)
            {
                //	Check if Task already exists
                int C_Task_ID = fromTasks[i].GetC_Task_ID();
                bool exists = false;
                if (C_Task_ID == 0)
                    exists = false;
                else
                {
                    for (int ii = 0; ii < myTasks.Length; ii++)
                    {
                        if (myTasks[ii].GetC_Task_ID() == C_Task_ID)
                        {
                            exists = true;
                            break;
                        }
                    }
                }
                //	Phase exist
                if (exists)
                {
                    log.Info("Task already exists here, ignored - " + fromTasks[i]);
                }
                else
                {
                    MProjectTask toTask = new MProjectTask(GetCtx(), 0, Get_TrxName());
                    PO.CopyValues(fromTasks[i], toTask, GetAD_Client_ID(), GetAD_Org_ID());
                    toTask.SetC_ProjectPhase_ID(GetC_ProjectPhase_ID());
                    if (toTask.Save())
                        count++;
                }
            }
            if (fromTasks.Length != count)
            {
                log.Warning("Count difference - ProjectPhase=" + fromTasks.Length + " <> Saved=" + count);
            }

            return count;
        }

        /// <summary>
        /// To copy task from other phase to new phase
        /// </summary>
        /// <param name="fromPhase">From Phase</param>
        /// <param name="toPhase">To Phase</param>
        /// <returns></returns>
        public int CopyTasksFrom(MProjectPhase fromPhase, MProjectPhase toPhase)
        {
            if (fromPhase == null)
                return 0;
            int count = 0;
            ValueNamePair pp = null; StringBuilder msg = new StringBuilder();
            MProjectTask[] myTasks = GetTasks();
            MProjectTask[] fromTasks = fromPhase.GetTasks();
            int C_Task_ID = 0; bool exists = false;
            //	Copy Project Tasks
            for (int i = 0; i < fromTasks.Length; i++)
            {
                //	Check if Task already exists
                C_Task_ID = fromTasks[i].GetC_ProjectTask_ID();
                exists = false;
                if (C_Task_ID == 0)
                    exists = false;
                else
                {
                    for (int ii = 0; ii < myTasks.Length; ii++)
                    {
                        if (myTasks[ii].GetC_ProjectTask_ID() == C_Task_ID)
                        {
                            exists = true;
                            break;
                        }
                    }
                }
                //	Phase exist
                if (exists)
                {
                    log.Info("Task already exists here, ignored - " + fromTasks[i]);
                }
                else
                {
                    MProjectTask toTask = new MProjectTask(GetCtx(), 0, Get_TrxName());
                    PO.CopyValues(fromTasks[i], toTask, GetAD_Client_ID(), GetAD_Org_ID());
                    toTask.SetC_ProjectPhase_ID(toPhase.GetC_ProjectPhase_ID());
                    if (toTask.Save())
                    {
                        count++;
                        count += CopyMTaskLinesFromProjectTask(fromTasks[i], toTask, toPhase.GetC_Project_ID());
                    }
                    else
                    {
                        pp = VLogger.RetrieveError();
                        if (pp != null)
                        {
                            msg.Append(pp.GetName());
                            //if GetName is Empty then it will check GetValue
                            if (string.IsNullOrEmpty(msg.ToString()))
                                msg.Append(Msg.GetMsg("", pp.GetValue()));
                        }
                        if (string.IsNullOrEmpty(msg.ToString()))
                            msg.Append(Msg.GetMsg(GetCtx(), "VIS_LineNotSaved"));
                        else
                            msg.Append(Msg.GetMsg(GetCtx(), "VIS_LineNotSaved") + "," + msg.ToString());
                    }
                }
            }
            if (fromTasks.Length != count)
            {
                log.Warning("Count difference - ProjectPhase=" + fromTasks.Length + " <> Saved=" + count);
            }

            return count;
        }

        /// <summary>
        /// Copy Tasks from other Phase
        /// </summary>
        /// <param name="fromPhase">from phase</param>
        /// <returns>number of tasks copied</returns>
        public int CopyTasksFrom(MProjectTypePhase fromPhase)
        {
            if (fromPhase == null)
                return 0;
            int count = 0;
            int tasklinecount = 0;
            //	Copy Type Tasks
            MProjectTypeTask[] fromTasks = fromPhase.GetTasks();
            for (int i = 0; i < fromTasks.Length; i++)
            {
                MProjectTask toTask = new MProjectTask(this, fromTasks[i]);
                if (toTask.Save())
                {
                    //VIS0336:for binding the dms documents on task tab of project window when inserting data from project template
                    if (Env.IsModuleInstalled("VA107_") && Env.IsModuleInstalled("VADMS_"))
                    {
                        string msg = GetAttachedDocuments(toTask.GetC_Task_ID(), toTask.GetC_ProjectTask_ID());
                        if (msg != "OK")
                        {
                            // return msg;
                        }
                    }

                    // check if table exists then only it will copy the task lines
                    if (PO.Get_Table_ID("C_TaskLine") > 0)
                        tasklinecount = CopyMTaskLines(fromTasks[i].GetC_Task_ID(), toTask.GetC_ProjectTask_ID());
                    count++;
                }
            }
            log.Fine("#" + count + " - " + fromPhase + ", #" + tasklinecount);
            if (fromTasks.Length != count)
            {
                log.Log(Level.SEVERE, "Count difference - TypePhase=" + fromTasks.Length + " <> Saved=" + count);
            }

            return count;
        }

        /// <summary>
        /// To copy the task lines from project template Standard Task lines tab
        /// </summary>
        /// <param name="Task_ID">ID of tasks</param>
        /// <param name="C_ProjectTask_ID">Project task ID</param>
        /// <returns>No of lines created</returns>
        public int CopyMTaskLines(int Task_ID, int C_ProjectTask_ID)
        {
            MProjectLine taskline = null;
            ValueNamePair pp = null;
            int tasklinecount = 0;
            StringBuilder msg = new StringBuilder();
            String sql = "SELECT M_Product_ID, Description, StandardQty, SeqNo FROM C_TaskLine WHERE IsActive='Y' AND C_Task_ID =" + Task_ID + " ORDER BY SeqNo";
            try
            {
                DataSet ds = DataBase.DB.ExecuteDataset(sql, null, Get_TrxName());
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                    {
                        taskline = new MProjectLine(GetCtx(), 0, Get_TrxName());
                        taskline.SetC_ProjectTask_ID(C_ProjectTask_ID);
                        taskline.SetDescription(Util.GetValueOfString(ds.Tables[0].Rows[i]["Description"]));
                        taskline.SetM_Product_ID(Util.GetValueOfInt(ds.Tables[0].Rows[i]["M_Product_ID"]));
                        taskline.SetPlannedQty(Util.GetValueOfDecimal(ds.Tables[0].Rows[i]["StandardQty"]));
                        taskline.Set_ValueNoCheck("TaskLineNo", Util.GetValueOfInt(ds.Tables[0].Rows[i]["SeqNo"]));
                        if (taskline.Save())
                        {
                            tasklinecount++;
                        }
                        else
                        {
                            pp = VLogger.RetrieveError();
                            if (pp != null)
                            {
                                msg.Append(pp.GetName());
                                //if GetName is Empty then it will check GetValue
                                if (string.IsNullOrEmpty(msg.ToString()))
                                    msg.Append(Msg.GetMsg("", pp.GetValue()));
                            }
                            if (string.IsNullOrEmpty(msg.ToString()))
                                msg.Append(Msg.GetMsg(GetCtx(), "VIS_TaskLineNotSaved"));
                            else
                                msg.Append(Msg.GetMsg(GetCtx(), "VIS_TaskLineNotSaved") + "," + msg.ToString());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Log(Level.SEVERE, sql, ex);
            }
            //
            return tasklinecount;
        }

        /// <summary>
        /// To copy the task lines from project template Standard Task lines tab
        /// </summary>
        /// <param name="Task_ID">ID of tasks</param>
        /// <param name="C_ProjectTask_ID">Project task ID</param>
        /// <returns>No of lines created</returns>
        public int CopyMTaskLinesFromProjectTask(MProjectTask fromTask, MProjectTask toTask, int To_Project_ID)
        {
            ValueNamePair pp = null;
            int tasklinecount = 0;
            StringBuilder msg = new StringBuilder();
            try
            {
                MProjectLine[] fromLines = null;
                List<MProjectLine> list = new List<MProjectLine>();

                DataSet projDs = DB.ExecuteDataset(" SELECT C_ProjectLine_ID FROM C_ProjectLine WHERE " +
                    " C_ProjectPhase_ID =" + fromTask.GetC_ProjectPhase_ID() + " AND C_ProjectTask_ID =" + fromTask.GetC_ProjectTask_ID() +
                    " AND IsActive='Y' ORDER BY Line ");

                if (projDs != null && projDs.Tables[0].Rows.Count > 0)
                {
                    for (int k = 0; k < projDs.Tables[0].Rows.Count; k++)
                    {
                        list.Add(new MProjectLine(GetCtx(), Util.GetValueOfInt(projDs.Tables[0].Rows[k]["C_ProjectLine_ID"]), Get_TrxName()));
                    }
                    fromLines = new MProjectLine[list.Count];
                    fromLines = list.ToArray();

                }

                if (fromLines != null && fromLines.Length > 0)
                {
                    for (int j = 0; j < fromLines.Length; j++)
                    {
                        MProjectLine line = new MProjectLine(GetCtx(), 0, Get_TrxName());
                        PO.CopyValues(fromLines[j], line, GetAD_Client_ID(), GetAD_Org_ID());
                        line.SetC_Project_ID(To_Project_ID);
                        line.SetC_ProjectTask_ID(toTask.GetC_ProjectTask_ID());
                        line.SetC_ProjectPhase_ID(toTask.GetC_ProjectPhase_ID());
                        line.SetInvoicedAmt(Env.ZERO);
                        line.SetInvoicedQty(Env.ZERO);
                        line.SetC_OrderPO_ID(0);
                        line.SetC_Order_ID(0);
                        line.SetProcessed(false);
                        if (line.Save())
                            tasklinecount++;
                        else
                        {
                            pp = VLogger.RetrieveError();
                            if (pp != null)
                            {
                                msg.Append(pp.GetName());
                                //if GetName is Empty then it will check GetValue
                                if (string.IsNullOrEmpty(msg.ToString()))
                                    msg.Append(Msg.GetMsg("", pp.GetValue()));
                            }
                            if (string.IsNullOrEmpty(msg.ToString()))
                                msg.Append(Msg.GetMsg(GetCtx(), "VIS_LineNotSaved"));
                            else
                                msg.Append(Msg.GetMsg(GetCtx(), "VIS_LineNotSaved") + "," + msg.ToString());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Log(Level.SEVERE, "", ex);
            }
            //
            return tasklinecount;
        }

        /// <summary>
        /// Get Project Phase Tasks.
        /// </summary>
        /// <returns>Array of tasks</returns>
        public MProjectTask[] GetTasks()
        {
            List<MProjectTask> list = new List<MProjectTask>();
            String sql = "SELECT * FROM C_ProjectTask WHERE C_ProjectPhase_ID=" + GetC_ProjectPhase_ID() + " ORDER BY SeqNo";
            try
            {
                DataSet ds = DataBase.DB.ExecuteDataset(sql, null, Get_TrxName());
                if (ds.Tables.Count > 0)
                {
                    foreach (DataRow rs in ds.Tables[0].Rows)
                    {
                        list.Add(new MProjectTask(GetCtx(), rs, Get_TrxName()));
                    }
                }
            }
            catch (Exception ex)
            {
                log.Log(Level.SEVERE, sql, ex);
            }
            //
            MProjectTask[] retValue = new MProjectTask[list.Count];
            retValue = list.ToArray();
            return retValue;
        }

        /// <summary>
        /// String Representation
        /// </summary>
        /// <returns>info</returns>
        public override String ToString()
        {
            StringBuilder sb = new StringBuilder("MProjectPhase[");
            sb.Append(Get_ID())
                .Append("-").Append(GetSeqNo())
                .Append("-").Append(GetName())
                .Append("]");
            return sb.ToString();
        }

        /// <summary>
        /// Before Save
        /// </summary>
        /// <param name="newRecord">new</param>
        /// <returns>true</returns>
        protected override bool BeforeSave(bool newRecord)
        {
            if (GetEndDate() < GetStartDate())
            {
                log.SaveError("", Msg.GetMsg(GetCtx(), "EnddategrtrthnStartdate"));
                return false;
            }
            return true;
        }

        /// <summary>
        /// After Save
        /// </summary>
        /// <param name="newRecord">new</param>
        /// <param name="success">success</param>
        /// <returns>success</returns>
        protected override bool AfterSave(bool newRecord, bool success)
        {
            if (!success)
            {
                return success;
            }

            //Used transaction object because total was not updating on header
            string isCam = Util.GetValueOfString(DB.ExecuteScalar("SELECT IsCampaign FROM C_Project WHERE C_Project_ID = " + GetC_Project_ID(), null, Get_TrxName()));
            string isOpp = Util.GetValueOfString(DB.ExecuteScalar("SELECT IsOpportunity FROM C_Project WHERE C_Project_ID = " + GetC_Project_ID(), null, Get_TrxName()));

            if (isOpp.Equals("N") && isCam.Equals("N"))
            {
                // set sum of total amount of phase tab to project tab, similalary Commitment amount                
                //project.SetPlannedAmt(Util.GetValueOfDecimal(DB.ExecuteScalar("SELECT COALESCE(SUM(pl.PlannedAmt),0) FROM C_Projectphase pl WHERE pl.IsActive = 'Y' AND pl.C_Project_ID = " + GetC_Project_ID(), null, Get_TrxName())));
                //project.SetCommittedAmt(Util.GetValueOfDecimal(DB.ExecuteScalar("SELECT COALESCE(SUM(pl.CommittedAmt),0) FROM C_Projectphase pl WHERE pl.IsActive = 'Y' AND pl.C_Project_ID = " + GetC_Project_ID(), null, Get_TrxName())));
                //if (!project.Save())
                //{
                //    log.Warning("Project not updated");
                //}

                // DevOps ID: 2786 - Planned Quantity not updated in Quantity field on header of Project screen from Phase line tab.
                MProject project = new MProject(GetCtx(), GetC_Project_ID(), Get_TrxName());
                StringBuilder Sql = new StringBuilder();
                if (project.GetProjectLineLevel().Equals("T") || project.GetProjectLineLevel().Equals("Y"))
                {
                    //VIS0060: Set sum of planned margin and planned quantity from Task line to Project tab
                    Sql.Append(@"SELECT COALESCE(SUM(pp.PlannedAmt),0) AS PlannedAmt, COALESCE(SUM(pp.CommittedAmt),0) AS CommittedAmt, 
                        SUM(pl.PlannedMarginAmt) AS PlannedMarginAmt, SUM(pl.PlannedQty) AS PlannedQty
                        FROM C_ProjectPhase pp INNER JOIN C_ProjectTask pt ON pp.C_Projectphase_ID = pt.C_Projectphase_ID 
                        INNER JOIN C_ProjectLine pl ON pl.C_ProjectTask_ID = pt.C_ProjectTask_ID
                        WHERE pp.C_Project_ID = " + project.GetC_Project_ID());
                }
                else
                {
                    //VIS0060: Set sum of planned margin and planned quantity from Phase line to Project tab
                    Sql.Append(@"SELECT COALESCE(SUM(pp.PlannedAmt),0) AS PlannedAmt, COALESCE(SUM(pp.CommittedAmt),0) AS CommittedAmt, 
                        SUM(pl.PlannedMarginAmt) AS PlannedMarginAmt, SUM(pl.PlannedQty) AS PlannedQty
                        FROM C_ProjectPhase pp INNER JOIN C_ProjectLine pl ON pl.C_ProjectPhase_ID = pp.C_ProjectPhase_ID
                        WHERE pp.C_Project_ID  = " + project.GetC_Project_ID());
                }
                DataSet ds = DB.ExecuteDataset(Sql.ToString(), null, Get_TrxName());
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    project.SetPlannedAmt(Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["PlannedAmt"]));
                    project.SetCommittedAmt(Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["CommittedAmt"]));
                    project.SetPlannedMarginAmt(Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["PlannedMarginAmt"]));
                    project.SetPlannedQty(Util.GetValueOfDecimal(ds.Tables[0].Rows[0]["PlannedQty"]));
                    if (!project.Save())
                    {
                        log.Warning("Project not updated");
                    }
                }
                ds.Dispose();
            }
            return true;
        }

       /// <summary>
       /// VIS0336:Method for fetching the attched documents in Task tab
       /// </summary>
       /// <param name="TaskRecordId"></param>
       /// <param name="ProjecTaskId"></param>
       /// <returns></returns>
        public string GetAttachedDocuments(int TaskRecordId, int ProjecTaskId)
        {
            string msg = "";
            if (ProjectTemplateWindowId == 0 || ProjectWindowId == 0)
            {
                StringBuilder sql = new StringBuilder();
                sql.Clear();
                sql.Append("SELECT AD_Window_ID From AD_Window WHERE Name='VA107_ProjectTemplates'");
                ProjectTemplateWindowId = Util.GetValueOfInt(DB.ExecuteScalar(sql.ToString(), null, Get_Trx()));
                sql.Clear();
                sql.Append("SELECT AD_Window_ID From AD_Window WHERE Name='VA107_Project'");
                ProjectWindowId = Util.GetValueOfInt(DB.ExecuteScalar(sql.ToString(), null, Get_Trx()));

            }

            string DocumentId = null;
            string Sql = @"SELECT WL.VADMS_Document_ID || '-' || MD.VADMS_MetaData_ID AS DocMetaID,WL.VADMS_Document_ID,D.Name FROM VADMS_Windowdoclink WL 
                            INNER JOIN VADMS_MetaData MD ON MD.VADMS_Document_ID = WL.VADMS_Document_ID INNER JOIN VADMS_Document D ON D.VADMS_Document_ID = WL.VADMS_Document_ID 
                            WHERE WL.Record_ID =" + TaskRecordId + " AND WL.AD_Table_ID =" + X_C_Task.Table_ID + " AND WL.AD_Window_ID =" + ProjectTemplateWindowId;
            DataSet DS = DB.ExecuteDataset(Sql, null, Get_Trx());
            if (DS != null && DS.Tables[0].Rows.Count > 0)
            {
                for (int j = 0; j < DS.Tables[0].Rows.Count; j++)
                {
                    DocumentId = Util.GetValueOfString(DS.Tables[0].Rows[j]["DocMetaID"]);
                    msg = AttachFrom(GetCtx(), DocumentId, ProjectWindowId, X_C_ProjectTask.Table_ID, ProjecTaskId);
                    if (msg != "OK")
                    {
                        // return msg;
                    }

                }
            }
            return msg;
        }

        /// <summary>
        /// VIS0336-for attaching the dms document on project window task tab whne project template process runs from project header
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="docID"></param>
        /// <param name="winID"></param>
        /// <param name="tableID"></param>
        /// <param name="recID"></param>
        /// <returns></returns>
        public string AttachFrom(Ctx ctx, string docID, int winID, int tableID, int recID)
        {
            bool isSuccessFullAttach = false;
            string[] strDocIds = docID.Split(',');
            string[] strMetaId = null;
            StringBuilder sql = new StringBuilder();

            if (strDocIds.Count() > 0)
            {
                for (int j = 0; j < strDocIds.Count(); j++)
                {
                    strMetaId = strDocIds[j].Split('-');

                    VAdvantage.Model.X_VADMS_WindowDocLink wlink = null;
                    wlink = new VAdvantage.Model.X_VADMS_WindowDocLink(ctx, 0, Get_Trx());
                    wlink.SetAD_Client_ID(ctx.GetAD_Client_ID());
                    wlink.SetAD_Org_ID(ctx.GetAD_Org_ID());
                    wlink.SetAD_Table_ID(tableID);
                    wlink.SetAD_Window_ID(winID);
                    wlink.SetRecord_ID(recID);
                    if (strDocIds[j].Trim() != string.Empty)
                    {
                        wlink.SetVADMS_Document_ID(Convert.ToInt32(strMetaId[0]));
                    }
                    if (wlink.Save())
                    {
                        X_VADMS_AttachMetaData objAttachMetaData = new X_VADMS_AttachMetaData(ctx, 0, Get_Trx());
                        objAttachMetaData.SetVADMS_WindowDocLink_ID(wlink.Get_ID());
                        objAttachMetaData.SetVADMS_Document_ID(wlink.GetVADMS_Document_ID());
                        objAttachMetaData.SetVADMS_MetaData_ID(Convert.ToInt32(strMetaId[1]));
                        if (objAttachMetaData.Save())
                        {
                            isSuccessFullAttach = true;
                        }
                        else
                        {
                            return Msg.GetMsg(GetCtx(), "NotSaved");
                        }
                    }
                    else
                    {
                        return Msg.GetMsg(GetCtx(), "NotSaved");
                    }
                }
            }
            if (!isSuccessFullAttach)
            {
                return Msg.GetMsg(GetCtx(), "DocumentNotAttach");
            }

            else
            {
                return "OK";
            }
        }

    }
}
