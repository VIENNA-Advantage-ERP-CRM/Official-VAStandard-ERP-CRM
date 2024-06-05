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
            String elementType = GetElementType();
            //	Natural Account
            if (ELEMENTTYPE_UserDefined.Equals(elementType) && IsNaturalAccount())
                SetIsNaturalAccount(false);
            //	Tree validation

            //VIS383:04/06/2024 DevOps TASK ID:5877:- When tree is not define then create new tree id behalf of element name 
            if (Util.GetValueOfInt(GetAD_Tree_ID()) == 0)
            {
                int treeId = 0;
                string msgError = CreateNewTree(GetCtx(), Get_Trx(), GetName(), out treeId);
                if (!string.IsNullOrEmpty(msgError))
                {
                    log.SaveError("FRPT_DuplicateRecord", "");
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
                output = "FRPT_DuplicateRecord";
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
                    output = "FRPT_NodeGenProcessNotFound";
                    treeID = 0;
                    return output;
                }

                MPInstance instance = new MPInstance(ctx, Convert.ToInt32(processID), 0);
                if (!instance.Save())
                {
                    output = "FRPT_ProcessNoInstance";
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
    }
}
