/********************************************************
 * Project Name   : VAdvantage
 * Class Name     : MElement
 * Purpose        : Accounting Element model.
 * Class Used     : MElement inherits from X_C_Element class
 * Chronological    Development
 * Raghunandan      08-May-2009
  ******************************************************/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VAdvantage.Process;
using VAdvantage.Classes;
using VAdvantage.Model;
using VAdvantage.DataBase;
using VAdvantage.SqlExec;
using System.Data;
using VAdvantage.Logging;
using VAdvantage.Utility;
using VAdvantage.ProcessEngine;
using System.IO;
using System.Web.Hosting;
using ViennaAdvantageWeb.Areas.VIS.Models;

namespace VAdvantage.Model
{
    public class MElement : X_C_Element
    {
        //Cache						
        private static CCache<int, MElement> s_cache = new CCache<int, MElement>("AD_Element", 20);
        // Tree Used		
        private X_AD_Tree _tree = null;

        /// <summary>
        ///Get Accounting Element from Cache
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="AD_Element_ID">id</param>
        /// <returns>MElement</returns>
        public static MElement Get(Ctx ctx, int AD_Element_ID)
        {
            int key = (int)AD_Element_ID;
            MElement retValue = (MElement)s_cache[key];
            if (retValue != null)
                return retValue;
            retValue = new MElement(ctx, AD_Element_ID, null);
            if (retValue.Get_ID() != 0)
                s_cache.Add(key, retValue);
            return retValue;
        }

        /// <summary>
        ///Standard Accounting Element Constructor
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="C_Element_ID">id</param>
        /// <param name="trxName">transaction</param>
        public MElement(Ctx ctx, int C_Element_ID, Trx trxName)
            : base(ctx, C_Element_ID, trxName)
        {
            if (C_Element_ID == 0)
            {
                //	setName (null);
                //	setAD_Tree_ID (0);
                //	setElementType (null);	// A
                SetIsBalancing(false);
                SetIsNaturalAccount(false);
            }
        }

        /// <summary>
        ///	Accounting Element Load Constructor
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="rs">result set</param>
        /// <param name="trxName">transaction</param>
        public MElement(Ctx ctx, DataRow rs, Trx trxName)
            : base(ctx, rs, trxName)
        {

        }

        /// <summary>
        ///Full Constructor
        /// </summary>
        /// <param name="client">client</param>
        /// <param name="Name">name</param>
        /// <param name="ElementType">type</param>
        /// <param name="AD_Tree_ID">tree</param>
        public MElement(MClient client, string name, string elementType, int AD_Tree_ID)
            : this(client.GetCtx(), 0, client.Get_TrxName())
        {
            SetClientOrg(client);
            SetName(name);
            SetElementType(elementType);	// A
            SetAD_Tree_ID(AD_Tree_ID);
            SetIsNaturalAccount(ELEMENTTYPE_Account.Equals(elementType));
        }

        /// <summary>
        ///Get Tree
        /// </summary>
        /// <returns>tree</returns>
        public X_AD_Tree GetTree()
        {
            if (_tree == null)
                _tree = new X_AD_Tree(GetCtx(), GetAD_Tree_ID(), Get_TrxName());
            return _tree;
        }

        /// <summary>
        ///	Before Save
        /// </summary>
        /// <param name="newRecord">newRecord new</param>
        /// <returns>true</returns>
        protected override bool BeforeSave(bool newRecord)
        {
            if (GetAD_Org_ID() != 0)
                SetAD_Org_ID(0);

            // VIS_045: 03-July-2024, Set Element Type as Account, because system was setting Account type based on default tree
            SetElementType(X_C_Element.ELEMENTTYPE_Account);
            String elementType = GetElementType();

            //	Natural Account
            if (ELEMENTTYPE_UserDefined.Equals(elementType) && IsNaturalAccount())
                SetIsNaturalAccount(false);
            //	Tree validation

            //VIS383:04/06/2024 DevOps TASK ID:5877:- When tree is not define then create new tree id behalf of element name 
            // when we are saving new record, system will create tree everytime, because for the newrecord, system was setting default tree record automatically.
            if (newRecord || Util.GetValueOfInt(GetAD_Tree_ID()) == 0)
            {
                int treeId = 0;
                string msgError = CreateNewTree(GetCtx(), Get_Trx(), GetName(), out treeId);
                if (!string.IsNullOrEmpty(msgError))
                {
                    log.SaveError(msgError, "");
                    return false;
                }
                SetAD_Tree_ID(treeId);
            }

            X_AD_Tree tree = GetTree();
            if (tree == null)
                return false;
            String treeType = tree.GetTreeType();
            if (ELEMENTTYPE_UserDefined.Equals(elementType))
            {
                if (X_AD_Tree.TREETYPE_User1.Equals(treeType) || X_AD_Tree.TREETYPE_User2.Equals(treeType))
                {
                    ;
                }
                else
                {
                    log.SaveError("Error", Msg.ParseTranslation(GetCtx(), "@TreeType@ <> @ElementType@ (U)"), false);
                    return false;
                }
            }
            else
            {
                if (!X_AD_Tree.TREETYPE_ElementValue.Equals(treeType))
                {
                    log.SaveError("Error", Msg.ParseTranslation(GetCtx(), "@TreeType@ <> @ElementType@ (A)"), false);
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// VIS383:04/06/2024 DevOps TASK ID:5877:-Create new tree and return Tree Id
        /// </summary>
        /// <param name="ctx">Context</param>
        /// <param name="trx">Transaction</param>
        /// <param name="name">Element Name</param>
        /// <param name="treeID">Tree ID</param>
        /// <returns>Return Tree ID</returns>
        public string CreateNewTree(Ctx ctx, Trx trx, string name, out int treeID)
        {
            string output = "";
            string treeName = name;
            string sql = "SELECT COUNT(*) FROM AD_Tree WHERE Lower(name) =Lower('" + treeName + "')";
            sql = MRole.Get(ctx, ctx.GetAD_Role_ID()).AddAccessSQL(sql, "AD_Tree", MRole.SQL_FULLYQUALIFIED, MRole.SQL_RO);
            int countRecords = Convert.ToInt32(DB.ExecuteScalar(sql));
            if (countRecords > 0)
            {
                output = "VAS_DuplicateRecord";
                treeID = 0;
                return output;
            }

            MTree newTree = new MTree(ctx, 0, trx);
            newTree.SetName(treeName);
            newTree.SetAD_Table_ID(MTable.Get_Table_ID("C_ElementValue"));
            newTree.SetTreeType("EV");
            newTree.SetIsAllNodes(true);
            if (newTree.Save(trx))
            {
                MClientInfo cInfo = new MClientInfo(ctx, GetAD_Client_ID(), null);
                sql = "SELECT AD_Process_ID FROM AD_Process WHERE Name='Verify Tree'";
                object processID = DB.ExecuteScalar(sql);
                if (processID == null || processID == DBNull.Value)
                {
                    output = "VAS_NodeGenProcessNotFound";
                    treeID = 0;
                    return output;
                }

                MPInstance instance = new MPInstance(ctx, Convert.ToInt32(processID), 0);
                if (!instance.Save())
                {
                    output = "VAS_ProcessNoInstance";
                    treeID = 0;
                    return output;
                }

                VAdvantage.ProcessEngine.ProcessInfo inf =
                    new VAdvantage.ProcessEngine.ProcessInfo("GenerateTreeNodes", Convert.ToInt32(processID), MTable.Get_Table_ID("AD_Tree"), newTree.GetAD_Tree_ID());
                inf.SetAD_PInstance_ID(instance.GetAD_PInstance_ID());
                inf.SetAD_Client_ID(GetAD_Client_ID());
                inf.SetRecord_ID(newTree.GetAD_Tree_ID());
                ProcessCtl worker = new ProcessCtl(ctx, null, inf, null);
                worker.Run();
                treeID = newTree.GetAD_Tree_ID();
                return output;
            }
            treeID = 0;
            return output;
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

            //VIS_045: 13-June-2024, TASK ID: 5913,  This logic is used to upload AccountingUS1.csv file as attachment from the Application Physical path for COA import.
            if (IsActive() && !IsRecordAlreadyAttached(GetC_Element_ID(), X_C_Element.Get_Table_ID("C_Element")))
            {
                AttachCOAImportFile("AccountingUS1.csv");
            }

            return base.AfterSave(newRecord, success);
        }

        /// <summary>
        /// This function is used to Attach file 
        /// </summary>
        /// <param name="fileName">FileName</param>
        /// <Author>VIS_045, 13-June-2024, TASK ID: 5913</Author>
        /// <returns>Error Detail (if any)</returns>
        public string AttachCOAImportFile(string fileName)
        {
            try
            {
                // Define the base directory path
                string baseDirectory = HostingEnvironment.ApplicationPhysicalPath;

                // Combine the base path with the file name
                string sourceFilePath = Path.Combine(baseDirectory, fileName);

                // Check if the file exists in the specified path
                if (File.Exists(sourceFilePath))
                {
                    // Get file information
                    FileInfo fileInfo = new FileInfo(sourceFilePath);

                    // Create a list to hold the file details
                    List<AttFileInfo> fileList = new List<AttFileInfo>
                {
                    new AttFileInfo
                    {
                        Name = fileInfo.Name,
                        Size = Util.GetValueOfInt(fileInfo.Length)
                    }
                };

                    #region Copy File into TempDownload Folder
                    // Define the temporary download directory path
                    string tempDownloadDirectory = Path.Combine(baseDirectory, "TempDownload");

                    // Ensure the temporary directory exists
                    if (!Directory.Exists(tempDownloadDirectory))
                    {
                        Directory.CreateDirectory(tempDownloadDirectory);
                    }

                    // Define the destination file path
                    string destinationFilePath = Path.Combine(tempDownloadDirectory, fileName);

                    // Copy the file to the destination path
                    File.Copy(sourceFilePath, destinationFilePath, true);
                    #endregion

                    // Attach the file (Pick file from TemDownload and attach)
                    AttachmentModel attachmentModel = new AttachmentModel();
                    AttachmentInfo attachmentInfo = attachmentModel.CreateAttachmentEntries(
                        fileList, 0, "", GetCtx(), X_C_Element.Get_Table_ID("C_Element"), GetC_Element_ID(), "", 0, false);

                    // Error Detail if not attached
                    if (attachmentInfo != null && !string.IsNullOrEmpty(attachmentInfo.Error))
                    {
                        log.Severe("File Not Attached For Chart of Account Name - " + GetName() + ", Error : " + attachmentInfo.Error);
                        return attachmentInfo.Error;
                    }
                }
            }
            catch { }

            return "";
        }

        /// <summary>
        /// This function is used to check, Attachment is already linked with the record or not
        /// </summary>
        /// <param name="Record_ID">Record ID</param>
        /// <param name="AD_Table_ID">Table ID</param>
        /// <Author>VIS_045, 13-June-2024, TASK ID: 5913</Author>
        /// <returns>True, when Exist</returns>
        public bool IsRecordAlreadyAttached(int Record_ID, int AD_Table_ID)
        {
            string sql = $@"SELECT COUNT(AD_Attachment_ID) FROM AD_Attachment WHERE AD_Table_ID = {AD_Table_ID} AND Record_ID = {Record_ID}";
            return (Util.GetValueOfInt(DB.ExecuteScalar(sql, null, Get_Trx())) > 0);
        }
    }
}
