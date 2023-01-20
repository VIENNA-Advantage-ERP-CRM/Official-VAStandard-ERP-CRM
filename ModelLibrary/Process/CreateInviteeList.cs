using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data;
using VAdvantage.ProcessEngine;
using VAdvantage.Utility;
using VAdvantage.Logging;
//using ViennaAdvantage.Model;
using VAdvantage.DataBase;
using System.Web.Hosting;
using System.Web;
using System.Security.Cryptography;
using System.Web.Security;
/* Process: Generate Invitee List 
 * Writer :Arpit Singh
 * Date   : 20/1/12 
 */
namespace ViennaAdvantageServer.Process
{

    class CreateInviteeList : SvrProcess
    {
        HttpApplication app = new HttpApplication();
        // int Record_ID;
        string url = "";
        protected override void Prepare()
        {
            url = GetCtx().GetContext("#ApplicationURL");
            url = url.ToLower();

            url = url.Replace("http://", "");
            url = url.Replace("https://", "");

            //if (url.Contains("https://"))
            //{

            //}
            if (url.Contains("/viennaadvantage.aspx"))
            {
                url = url.Substring(0, url.LastIndexOf("/")).ToString() + "/CampaignInvitee.aspx";
            }
            else
            {
                url = url + "/CampaignInvitee.aspx";
            }
#pragma warning disable 612, 618
            object o = System.Configuration.ConfigurationSettings.AppSettings["IsSSLEnabled"];
#pragma warning restore 612, 618
            if (o != null && o.ToString() == "Y")
            {
                url = "https://" + url;
            }
            else
            {
                url = "http://" + url;
            }


        }

        protected override String DoIt()
        {
            // VIS0060: Work done to handle DataReader Issue on Postgre Database.
            StringBuilder query = new StringBuilder("SELECT C_CampaignTargetList_ID FROM C_CampaignTargetList WHERE C_Campaign_ID=" + GetRecord_ID() + " AND AD_Client_ID = "
                + GetCtx().GetAD_Client_ID());
            DataSet MainDs = DB.ExecuteDataset(query.ToString(), null, Get_Trx());
            DataSet ds = null;

            try
            {
                query.Clear();
                query.Append("Delete From C_InviteeList WHERE C_Campaign_ID=" + GetRecord_ID());
                int value = DB.ExecuteQuery(query.ToString());
                if (MainDs != null && MainDs.Tables.Count > 0 && MainDs.Tables[0].Rows.Count > 0)
                {
                    foreach (DataRow MainDr in MainDs.Tables[0].Rows)
                    {
                        int id = Util.GetValueOfInt(MainDr[0]);
                        VAdvantage.Model.X_C_CampaignTargetList MCapTarget = new VAdvantage.Model.X_C_CampaignTargetList(GetCtx(), id, null);

                        if (MCapTarget.GetC_MasterTargetList_ID() != 0)
                        {
                            query.Clear();
                            query.Append("SELECT C_BPartner_ID FROM C_TargetList WHERE C_MasterTargetList_ID=" + MCapTarget.GetC_MasterTargetList_ID() + " AND C_BPartner_ID IS NOT NULL");
                            ds = DB.ExecuteDataset(query.ToString(), null, Get_Trx());
                            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                            {
                                foreach (DataRow dr in ds.Tables[0].Rows)
                                {
                                    invitee(Util.GetValueOfInt(dr[0]));
                                }
                            }

                            query.Clear();
                            query.Append("SELECT Ref_BPartner_ID FROM C_TargetList WHERE C_MasterTargetList_ID=" + MCapTarget.GetC_MasterTargetList_ID() + " AND Ref_BPartner_ID IS NOT NULL");
                            ds = DB.ExecuteDataset(query.ToString(), null, Get_Trx());
                            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                            {
                                foreach (DataRow dr in ds.Tables[0].Rows)
                                {
                                    invitee(Util.GetValueOfInt(dr[0]));
                                }
                            }

                            query.Clear();
                            query.Append("SELECT C_Lead_ID FROM C_TargetList WHERE C_MasterTargetList_ID=" + MCapTarget.GetC_MasterTargetList_ID() + " AND C_Lead_ID IS NOT NULL");
                            ds = DB.ExecuteDataset(query.ToString(), null, Get_Trx());
                            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                            {
                                foreach (DataRow dr in ds.Tables[0].Rows)
                                {
                                    query.Clear();
                                    string sql = "SELECT C_InviteeList_ID FROM C_InviteeList WHERE C_Lead_ID=" + Util.GetValueOfInt(dr[0]);
                                    object Leadid = DB.ExecuteScalar(sql, null, Get_Trx());
                                    if (Util.GetValueOfInt(Leadid) == 0)
                                    {
                                        VAdvantage.Model.X_C_Lead lead = new VAdvantage.Model.X_C_Lead(GetCtx(), Util.GetValueOfInt(dr[0]), Get_Trx());
                                        if (lead.GetC_BPartner_ID() != 0)
                                        {
                                            invitee(lead.GetC_BPartner_ID());
                                        }
                                        else if (lead.GetRef_BPartner_ID() != 0)
                                        {
                                            invitee(lead.GetRef_BPartner_ID());
                                        }
                                        else if (lead.GetContactName() != null)
                                        {
                                            VAdvantage.Model.X_C_InviteeList Invt = new VAdvantage.Model.X_C_InviteeList(GetCtx(), 0, Get_Trx());
                                            //Invt.SetC_TargetList_ID(Util.GetValueOfInt(dr[0]));
                                            Invt.SetC_Campaign_ID(GetRecord_ID());
                                            Invt.SetName(lead.GetContactName());
                                            Invt.SetEMail(lead.GetEMail());
                                            Invt.SetPhone(lead.GetPhone());
                                            Invt.SetC_Lead_ID(lead.GetC_Lead_ID());
                                            Invt.SetAddress1(lead.GetAddress1());
                                            Invt.SetAddress1(lead.GetAddress2());
                                            Invt.SetC_City_ID(lead.GetC_City_ID());
                                            Invt.SetCity(lead.GetCity());
                                            Invt.SetC_Region_ID(lead.GetC_Region_ID());
                                            Invt.SetRegionName(lead.GetRegionName());
                                            Invt.SetC_Country_ID(lead.GetC_Country_ID());
                                            Invt.SetPostal(lead.GetPostal());
                                            // Invt.SetURL(url);
                                            if (!Invt.Save())
                                            {
                                                Msg.GetMsg(GetCtx(), "InviteeCteationNotDone");
                                            }

                                            string ID = Invt.GetC_InviteeList_ID().ToString();
                                            string encrypt = FormsAuthentication.HashPasswordForStoringInConfigFile(ID, "SHA1");
                                            string urlFinal = "";
                                            urlFinal = url + "?" + encrypt;
                                            sql = "update c_inviteelist set url = '" + urlFinal + "' where c_inviteelist_id = " + Invt.GetC_InviteeList_ID();
                                            int res = Util.GetValueOfInt(DB.ExecuteQuery(sql, null, Get_Trx()));

                                            //Random rand = new Random();
                                            //String s = "";
                                            //for (int i = 0; i < 9; i++)
                                            //    s = String.Concat(s, rand.Next(10).ToString());
                                            //string urlFinal = "";
                                            //// urlFinal = url + "?" + Invt.GetC_InviteeList_ID().ToString();
                                            //urlFinal = url + "?" + s;
                                            ////string urlFinal = "";
                                            ////urlFinal = url + "?" + Invt.GetC_InviteeList_ID().ToString();
                                            //Invt.SetURL(urlFinal);
                                            //if (!Invt.Save(Get_Trx()))
                                            //{
                                            //    Msg.GetMsg(GetCtx(), "InviteeCteationNotDone");
                                            //}

                                        }
                                    }

                                }
                            }
                        }

                        if (MCapTarget.GetR_InterestArea_ID() != 0)
                        {
                            query.Clear();
                            query.Append("SELECT C_BPartner_ID FROM R_ContactInterest WHERE R_InterestArea_ID=" + MCapTarget.GetR_InterestArea_ID() + " AND C_BPartner_ID IS NOT NULL");
                            ds = DB.ExecuteDataset(query.ToString(), null, Get_Trx());
                            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                            {
                                foreach (DataRow dr in ds.Tables[0].Rows)
                                {
                                    invitee(Util.GetValueOfInt(dr[0]));
                                }
                            }

                            //query = "Select C_BPartner_ID from C_TargetList where R_InterestArea_ID=" + MCapTarget.GetR_InterestArea_ID();
                            //dr = DB.ExecuteReader(query);
                            //while (dr.Read())
                            //{
                            //    invitee(Util.GetValueOfInt(dr[0]));

                            //}
                            //dr.Close();

                            query.Clear();
                            query.Append("SELECT C_Lead_ID FROM vss_lead_interestarea WHERE R_InterestArea_ID=" + MCapTarget.GetR_InterestArea_ID() + " AND C_Lead_ID IS NOT NULL");
                            ds = DB.ExecuteDataset(query.ToString(), null, Get_Trx());
                            if (ds != null && ds.Tables.Count > 0 && ds.Tables[0].Rows.Count > 0)
                            {
                                foreach (DataRow dr in ds.Tables[0].Rows)
                                {
                                    query.Clear();
                                    query.Append("SELECT C_InviteeList_ID FROM C_InviteeList WHERE C_Lead_ID=" + Util.GetValueOfInt(dr[0]));
                                    object Leadid = DB.ExecuteScalar(query.ToString(), null, Get_Trx());
                                    if (Util.GetValueOfInt(Leadid) == 0)
                                    {
                                        VAdvantage.Model.X_C_Lead lead = new VAdvantage.Model.X_C_Lead(GetCtx(), Util.GetValueOfInt(dr[0]), Get_Trx());
                                        if (lead.GetC_BPartner_ID() != 0)
                                        {
                                            invitee(lead.GetC_BPartner_ID());

                                        }
                                        else if (lead.GetRef_BPartner_ID() != 0)
                                        {
                                            invitee(lead.GetRef_BPartner_ID());
                                        }

                                        else if (lead.GetContactName() != null)
                                        {
                                            VAdvantage.Model.X_C_InviteeList Invt = new VAdvantage.Model.X_C_InviteeList(GetCtx(), 0, Get_Trx());
                                            //Invt.SetC_TargetList_ID(Util.GetValueOfInt(dr[0]));
                                            Invt.SetC_Campaign_ID(GetRecord_ID());
                                            Invt.SetName(lead.GetContactName());
                                            Invt.SetEMail(lead.GetEMail());
                                            Invt.SetPhone(lead.GetPhone());
                                            Invt.SetC_Lead_ID(lead.GetC_Lead_ID());
                                            Invt.SetAddress1(lead.GetAddress1());
                                            Invt.SetAddress1(lead.GetAddress2());
                                            Invt.SetC_City_ID(lead.GetC_City_ID());
                                            Invt.SetCity(lead.GetCity());
                                            Invt.SetC_Region_ID(lead.GetC_Region_ID());
                                            Invt.SetRegionName(lead.GetRegionName());
                                            Invt.SetC_Country_ID(lead.GetC_Country_ID());
                                            Invt.SetPostal(lead.GetPostal());
                                            //Invt.SetURL(url);
                                            if (!Invt.Save(Get_Trx()))
                                            {
                                                Msg.GetMsg(GetCtx(), "InviteeCteationNotDone");
                                            }

                                            string ID = Invt.GetC_InviteeList_ID().ToString();
                                            string encrypt = FormsAuthentication.HashPasswordForStoringInConfigFile(ID, "SHA1");
                                            string urlFinal = "";
                                            urlFinal = url + "?" + encrypt;
                                            query.Clear();
                                            query.Append("UPDATE C_InviteeList SET URL = '" + urlFinal + "' WHERE C_InviteeList_ID = " + Invt.GetC_InviteeList_ID());
                                            int res = Util.GetValueOfInt(DB.ExecuteQuery(query.ToString(), null, Get_Trx()));

                                            //Random rand = new Random();
                                            //String s = "";
                                            //for (int i = 0; i < 9; i++)
                                            //    s = String.Concat(s, rand.Next(10).ToString());
                                            //string urlFinal = "";
                                            //urlFinal = url + "?" + s;
                                            ////string urlFinal = "";
                                            ////urlFinal = url + "?" + Invt.GetC_InviteeList_ID().ToString();
                                            //Invt.SetURL(urlFinal);
                                            //if (!Invt.Save(Get_Trx()))
                                            //{
                                            //    Msg.GetMsg(GetCtx(), "InviteeCteationNotDone");
                                            //}


                                        }
                                    }

                                }
                            }
                        }
                    }
                }
                //MainDr.Close();
            }
            catch
            {
                //if (MainDr != null)
                //{
                //    MainDr.Close();
                //    MainDr = null;
                //}
                //if (dr != null)
                //{
                //    dr.Close();
                //    dr = null;
                //}
            }
            return Msg.GetMsg(GetCtx(), "InviteeCteationDone");
        }


        public void invitee(int bpid)
        {
            int AD_Id = 0;
            string name = "", email = "", phone = "";
            // VAdvantage.Model.X_C_BPartner bp = new VAdvantage.Model.X_C_BPartner(GetCtx(), bpid, Get_Trx());
            StringBuilder query = new StringBuilder("SELECT Ad_User_ID, Name, EMail, Phone FROM Ad_User WHERE C_BPartner_ID=" + bpid);
            DataSet dsUser = DB.ExecuteDataset(query.ToString(), null, Get_Trx());
            if (dsUser != null && dsUser.Tables.Count > 0 && dsUser.Tables[0].Rows.Count > 0)
            {
                AD_Id = Util.GetValueOfInt(dsUser.Tables[0].Rows[0]["Ad_User_ID"]);
                name = Util.GetValueOfString(dsUser.Tables[0].Rows[0]["Name"]);
                email = Util.GetValueOfString(dsUser.Tables[0].Rows[0]["Email"]);
                phone = Util.GetValueOfString(dsUser.Tables[0].Rows[0]["Phone"]);
            }

            query.Clear();
            query.Append("SELECT C_InviteeList_ID FROM C_InviteeList WHERE Ad_User_ID=" + AD_Id + " AND C_Campaign_ID=" + GetRecord_ID());
            object id = DB.ExecuteScalar(query.ToString(), null, Get_Trx());
            VAdvantage.Model.X_C_InviteeList Invt;
            if (Util.GetValueOfInt(id) != 0)
            {
                Invt = new VAdvantage.Model.X_C_InviteeList(GetCtx(), Util.GetValueOfInt(id), Get_Trx());
            }
            else
            {
                Invt = new VAdvantage.Model.X_C_InviteeList(GetCtx(), 0, Get_Trx());
                Invt.SetAD_User_ID(AD_Id);
            }

            query.Clear();
            query.Append("SELECT C_Location_ID FROM C_BPartner_Location WHERE C_BPartner_ID=" + bpid);
            int _location = Util.GetValueOfInt(DB.ExecuteScalar(query.ToString(), null, Get_Trx()));
            if (_location != 0)
            {
                Invt.SetC_Location_ID(_location);
            }

            //VAdvantage.Model.X_AD_User user = new VAdvantage.Model.X_AD_User(GetCtx(), AD_Id, Get_Trx());            
            Invt.SetC_Campaign_ID(GetRecord_ID());
            Invt.SetName(name);
            Invt.SetEMail(email);
            Invt.SetPhone(phone);
            // Invt.SetURL(url);
            if (!Invt.Save(Get_Trx()))
            {
                Msg.GetMsg(GetCtx(), "InviteeCteationNotDone");
            }

            string ID = Invt.GetC_InviteeList_ID().ToString();
            string encrypt = FormsAuthentication.HashPasswordForStoringInConfigFile(ID, "SHA1");
            string urlFinal = "";
            urlFinal = url + "?" + encrypt;

            query.Clear();
            query.Append("UPDATE C_InviteeList SET URL = '" + urlFinal + "' WHERE C_InviteeList_ID = " + Invt.GetC_InviteeList_ID());
            int res = Util.GetValueOfInt(DB.ExecuteQuery(query.ToString(), null, Get_Trx()));            
        }
    }
}
