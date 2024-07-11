/********************************************************
 * Project Name   : VAdvantage
 * Class Name     : ProjectSetType
 * Purpose        : Set Project Type
 * Class Used     : ProcessEngine.SvrProcess
 * Chronological    Development
 * Deepak           07-Dec-2009
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
using VAdvantage.Logging;

using VAdvantage.ProcessEngine;
using ModelLibrary.Model;

namespace ViennaAdvantage.Process
{
    public class ProjectSetType:VAdvantage.ProcessEngine.SvrProcess
    {
        /**	Project directly from Project	*/
        private int				_C_Project_ID = 0;
        /** Project Type Parameter			*/
        private int				_C_ProjectType_ID = 0;

        /// <summary>
        /// Prepare - e.g., get Parameters.
        /// </summary>
        protected override void Prepare()
        {
            ProcessInfoParameter[] para = GetParameter();
            for (int i = 0; i < para.Length; i++)
            {
                String name = para[i].GetParameterName();
                if (para[i].GetParameter() == null)
                {
                    continue;
                }
                else if (name.Equals("C_ProjectType_ID"))
                {
                    //_C_ProjectType_ID = ((Decimal)para[i].GetParameter()).intValue();
                    _C_ProjectType_ID = Util.GetValueOfInt(Util.GetValueOfDecimal(para[i].GetParameter()));
                }
                else
                {
                    log.Log(Level.SEVERE, "prepare - Unknown Parameter: " + name);
                }
              }
        }	//	prepare

        /// <summary>
        /// Perrform Process.
        /// </summary>
        /// <returns>Message (clear text)</returns>
        protected override String DoIt()
        {
            _C_Project_ID = GetRecord_ID();
            log.Info("doIt - C_Project_ID=" + _C_Project_ID + ", C_ProjectType_ID=" + _C_ProjectType_ID);
            //
            MProject project = new MProject (GetCtx(), _C_Project_ID, Get_Trx());
            if (project.GetC_Project_ID() == 0 || project.GetC_Project_ID() != _C_Project_ID)
            {
                throw new ArgumentException("Project not found C_Project_ID=" + _C_Project_ID);
            }

            if (project.GetC_ProjectType_ID_Int() > 0)
            {
                throw new ArgumentException("Project already has Type (Cannot overwrite) " + project.GetC_ProjectType_ID());
            }
            //
            MProjectType type = new MProjectType (GetCtx(), _C_ProjectType_ID, Get_Trx());
            if (type.GetC_ProjectType_ID() == 0 || type.GetC_ProjectType_ID() != _C_ProjectType_ID)
            {
                throw new ArgumentException("Project Type not found C_ProjectType_ID=" + _C_ProjectType_ID);
            }
            //	Set & Copy if Service
            project.SetProjectType(type);

            
            //Copy Module and Documents along with Phase n Task on Project from Project Template
            //Dev Opps ID=5995
            if (Env.IsModuleInstalled("VA107_"))
            {
                PO pm = null;
               
                string sql = @"SELECT pm.ad_client_id as pmClientID,pm.ad_org_id as pmOrgID,pm.c_projecttype_id as pmProjectType,pm.description as pmDescription,pm.va107_moduleversion_id as pmModuleVersion , pm.va107_module_id as pmModule,pm.va107_projecttempmodule_id as pmProjecttempModule,pd.ad_client_id as pdClientId,pd.ad_org_id as pdOrgId,pd.va107_documenttype as pdDocumentType,pd.va107_downloadurl as pdDownloadUrl,pd.va107_moduledocument_id as pdModeuleDocument,pd.va107_projecttempdocument_id as pdProjectTempDocument,pd.va107_projecttempmodule_id as pdProjectTempModule
                            FROM VA107_ProjectTempModule pm LEFT JOIN VA107_ProjectTempDocument pd ON ( pm.VA107_ProjectTempModule_ID = pd.VA107_ProjectTempModule_ID ) WHERE pm.c_projecttype_id = " + _C_ProjectType_ID + "ORDER BY pmProjecttempModule";
                DataSet ds = DB.ExecuteDataset(sql);
                if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                {
                    int SeqNodoc = 0;
                    int SeqNo = Util.GetValueOfInt(DB.ExecuteScalar("SELECT NVL(MAX(Line),0) FROM VA107_ProjectModule WHERE C_Project_ID=" + _C_Project_ID, null, Get_Trx()));
                    for (int i = 0; i < ds.Tables[0].Rows.Count; i++)
                    {

                        if (i > 0 && (Util.GetValueOfInt(ds.Tables[0].Rows[i]["pmProjecttempModule"]) == Util.GetValueOfInt(ds.Tables[0].Rows[i - 1]["pmProjecttempModule"])))
                        {
                            //to not copy project module every time
                        }
                        else
                        {
                             pm = MTable.GetPO(GetCtx(), "VA107_ProjectModule", 0, Get_Trx());
                            SeqNodoc = 0;
                            pm.Set_ValueNoCheck("C_Project_ID", GetRecord_ID());
                            pm.SetAD_Client_ID(Util.GetValueOfInt(ds.Tables[0].Rows[i]["pmClientID"]));
                            pm.SetAD_Org_ID(Util.GetValueOfInt(ds.Tables[0].Rows[i]["pmOrgID"]));
                            pm.Set_Value("VA107_Module_ID", Util.GetValueOfInt(ds.Tables[0].Rows[i]["pmModule"]));
                            pm.Set_Value("VA107_ModuleVersion_ID",Util.GetValueOfInt(ds.Tables[0].Rows[i]["pmModuleVersion"]));
                            pm.Set_Value("Description",Util.GetValueOfString(ds.Tables[0].Rows[0]["pmDescription"]));
                            SeqNo = SeqNo + 10;
                            pm.Set_Value("Line", SeqNo);
                            if (!pm.Save(Get_Trx()))
                            {
                                Get_Trx().Rollback();
                                ValueNamePair v = VLogger.RetrieveError();
                                if (v != null)
                                {
                                    return Msg.GetMsg(GetCtx(), "VA107_ProjectModuleNotSaved") + ":" + v.Name;     
                                }
                                return Msg.GetMsg(GetCtx(), "VA107_ProjectModuleNotSaved");
                            }
                        }

                        if (Util.GetValueOfInt(ds.Tables[0].Rows[i]["pdProjectTempDocument"]) > 0)
                        {
                            //pm.GetValueAsString
                            PO pd = MTable.GetPO(GetCtx(), "VA107_ProjectDocument", 0, Get_Trx());
                            pd.Set_ValueNoCheck("VA107_ProjectModule_ID", pm.GetValueAsString("VA107_ProjectModule_ID"));
                            pd.SetAD_Client_ID(Util.GetValueOfInt(ds.Tables[0].Rows[i]["pdClientId"]));
                            pd.SetAD_Org_ID(Util.GetValueOfInt(ds.Tables[0].Rows[i]["pdOrgId"]));
                            pd.Set_Value("VA107_ModuleDocument_ID", Util.GetValueOfInt(ds.Tables[0].Rows[i]["pdModeuleDocument"]));
                            pd.Set_Value("VA107_DocumentType", Util.GetValueOfString(ds.Tables[0].Rows[i]["pdDocumentType"]));
                            pd.Set_Value("VA107_DownloadURL", Util.GetValueOfString(ds.Tables[0].Rows[i]["pdDownloadUrl"]));
                            //int SeqNodoc = Util.GetValueOfInt(DB.ExecuteScalar("SELECT NVL(MAX(Line),0) FROM VA107_ProjectDocument WHERE VA107_ProjectModule_ID=" + mProjectModule.GetVA107_ProjectModule_ID(), null, Get_Trx())) + 10;
                            SeqNodoc = SeqNodoc + 10;
                            pd.Set_Value("Line", SeqNodoc);
                            if (!pd.Save(Get_Trx()))
                            {
                                Get_Trx().Rollback();
                                ValueNamePair v = VLogger.RetrieveError();
                                if (v != null)
                                {
                                    return Msg.GetMsg(GetCtx(), "VA107_ProjectDocumentNotSaved") + ":" + v.Name;
                                }
                                return Msg.GetMsg(GetCtx(), "VA107_ProjectDocumentNotSaved");
                            }
                        }
                    }
                }
            }
            if (!project.Save())
            {
               return GetRetrievedError(project, "@Error@");
               // throw new Exception("@Error@");
            }
            //
            return "@OK@";
        }	//	doIt

}	//	ProjectSetType

}
