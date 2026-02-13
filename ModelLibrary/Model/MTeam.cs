/********************************************************
 * Project Name   : VAdvantage
 * Class Name     : MTeam
 * Purpose        : Class linked with the Team
 * Class Used     : X_C_Team
 * Chronological  : Development
 * author         : VIS_427
  ******************************************************/

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using ViennaAdvantage.Model;

namespace VAdvantage.Model
{
    public class MTeam : X_C_Team
    {
        int teamLevel = 0;
        int parentTeamId = 0;

        public MTeam(Ctx ctx, DataRow dr, Trx trx)
            : base(ctx, dr, trx)
        {

        }

        public MTeam(Ctx ctx, int C_Team_ID, Trx trx)
            : base(ctx, C_Team_ID, trx)
        {

        }
        /// <summary>
        /// This fucntion to after the save of records in window
        /// </summary>
        /// <param name="newRecord"></param>
        /// <param name="success"></param>
        /// <returns></returns>
        protected override bool AfterSave(bool newRecord, bool success)
        {
            if (!success)
                return success;

            if (Env.IsModuleInstalled("VA137_"))
            {
                //Level-2 Team ID
                int level2TeamId = GetLevel2TeamID(parentTeamId, teamLevel);
                if (level2TeamId > 0)
                {
                    int count = DB.ExecuteQuery("UPDATE C_TEAM SET VA137_LEVEL2TEAM_ID=" + level2TeamId + " WHERE C_Team_ID=" + GetC_Team_ID(), null, Get_Trx());
                }
            }
            return true;
        }
        /// <summary>
        /// This function is used to generate the team code
        /// </summary>
        /// <param name="parentTeamId"></param>
        /// <param name="teamType"></param>
        /// <returns>Team Code</returns>
        /// <author>VIS_427</author>
        private string GenerateTeamCode(int parentTeamId, string teamType)
        {
            string parentCode = "";
            int nextNo;

            /* ================= PARENT CODE (ONLY FOR STRUCTURE) ================= */
            if (parentTeamId > 0)
            {
                parentCode = Util.GetValueOfString(DB.ExecuteScalar(@"
            SELECT VALUE
            FROM C_TEAM
            WHERE C_TEAM_ID = " + parentTeamId, null, Get_Trx()));
            }
            /* ================= LEVEL 1 ================= */
            if (parentTeamId <= 0)
            {
                nextNo = Util.GetValueOfInt(DB.ExecuteScalar(@"
            SELECT NVL(COUNT(*),0) + 1
            FROM C_TEAM
            WHERE VA137_TEAMTYPE = '" + teamType + @"'
            AND VA137_TeamLevel ='" + Util.GetValueOfString(Get_Value("VA137_TeamLevel")) + "'", null, Get_Trx()));

                return teamType + "-" + nextNo;
            }
            //next number for other level
            else
            {
                string sql = @"SELECT NVL(COUNT(*),0) + 1
                         FROM C_TEAM c
                         WHERE c.VA137_TEAM_ID IN (
                             SELECT C_TEAM_ID
                             FROM C_TEAM
                             WHERE VA137_TeamLevel = (
                                 SELECT VA137_TeamLevel
                                 FROM C_TEAM
                                 WHERE C_TEAM_ID = " + parentTeamId + @")
                             AND VA137_TEAMTYPE = (
                                 SELECT VA137_TEAMTYPE
                                 FROM C_TEAM
                                 WHERE C_TEAM_ID = " + parentTeamId + @")) AND c.C_TEAM_ID <> " + GetC_Team_ID();
                nextNo = Util.GetValueOfInt(DB.ExecuteScalar(sql, null, Get_Trx()));
            }

            /* ================= CHILD TEAM ================= */
            return parentCode + "-" + nextNo + (GetVA137_SourceType() == "OO" ? "-"+ GetVA137_SourceType():"");
        }




        /// <summary>
        /// This function used to get team id of level2 team
        /// </summary>
        /// <param name="parentTeamId"></param>
        /// <param name="teamLevel"></param>
        /// <returns>Level 2 Team ID</returns>
        /// <author>VIS_427</author>
        private int GetLevel2TeamID(int parentTeamId, int teamLevel)
        {
            // Level 1
            if (teamLevel == 1)
                return 0;

            // Level 2
            if (teamLevel == 2)
                return GetC_Team_ID(); // self

            // Level 3+
            string parentCode = Util.GetValueOfString(DB.ExecuteScalar(@"
        SELECT VALUE
        FROM C_TEAM
        WHERE C_TEAM_ID = " + parentTeamId, null, Get_Trx()));

            string[] parts = parentCode.Split('-');

            if (parts.Length < 3)
                return 0;

            string level2Code = parts[0] + "-" + parts[1] + "-" + parts[2];

            return Util.GetValueOfInt(DB.ExecuteScalar(@"
        SELECT C_TEAM_ID
        FROM C_TEAM
        WHERE VALUE = '" + level2Code + @"'", null, Get_Trx()));
        }
        /// <summary>
        /// This function used to execute functionality before the save of window
        /// </summary>
        /// <param name="newRecord"></param>
        /// <returns></returns>
        protected override bool BeforeSave(bool newRecord)
        {
            if (Env.IsModuleInstalled("VA137_"))
            {
                 parentTeamId = GetVA137_Team_ID();          // selected parent
                //if Source type is oustsid organization then return false
                if (newRecord || Is_ValueChanged("VA137_SourceType") || Is_ValueChanged("VA137_TEAM_ID"))
                {
                    if (parentTeamId == 0 && GetVA137_SourceType() == "OO")
                    {
                        log.SaveError("", Msg.GetMsg(GetCtx(), "VA137_OutsideOrgParentNotCreate"));
                        return false;
                    }
                    else if (GetVA137_SourceType() == "OO")
                    {
                        string sql = @"SELECT VA137_TeamLevel
                                 FROM C_TEAM
                                 WHERE C_TEAM_ID = " + parentTeamId;
                        if (string.IsNullOrEmpty(Util.GetValueOfString(DB.ExecuteScalar(sql, null, Get_Trx()))))
                        {
                            log.SaveError("", Msg.GetMsg(GetCtx(), "VA137_OutsideOrgChildNotCreate"));
                            return false;
                        }
                    }
                }
                    int oldParentTeamId = Util.GetValueOfInt(Get_ValueOld("VA137_Team_ID"));

                /* ================= TEAM LEVEL ================= */
                teamLevel = Util.GetValueOfInt(DB.ExecuteScalar(@"
        SELECT VA137_TEAMLEVEL
        FROM VA137_PickListLine
        WHERE Value ='" + Util.GetValueOfString(Get_Value("VA137_TeamLevel")) + "'", null, Get_Trx()));

                /* ================= PARENT CHANGE VALIDATION ================= */
                if (!newRecord && parentTeamId != oldParentTeamId)
                {
                    int childCount = Util.GetValueOfInt(DB.ExecuteScalar(@"
            SELECT COUNT(C_Team_ID)
            FROM C_TEAM
            WHERE VA137_TEAM_ID = " + GetC_Team_ID()));

                    if (childCount > 0)
                    {
                        log.SaveError("",Msg.GetMsg(GetCtx(), "VA137_ChildExistRecordNotSaved"));
                        return false;
                    }
                }
                if (newRecord || Is_ValueChanged("VA137_TEAMLEVEL") || Is_ValueChanged("VA137_Team_ID") || Is_ValueChanged("VA137_SourceType")) 
                {
                    string teamCode = GenerateTeamCode(parentTeamId, GetVA137_TeamType());
                    SetValue(teamCode) ;
                }
            }

            return true;
        }
        /// <summary>
        /// This function is used to delete the record
        /// </summary>
        /// <returns>true</returns>
        protected override bool BeforeDelete()
        {
            if (Env.IsModuleInstalled("VA137_"))
            {
                int childCount = Util.GetValueOfInt(DB.ExecuteScalar(@"
        SELECT COUNT(C_TEAM_ID)
        FROM C_TEAM
        WHERE VA137_TEAM_ID = " + GetC_Team_ID(), null, Get_Trx()));

                if (childCount > 0)
                {
                    log.SaveError("", Msg.GetMsg(GetCtx(), "VA137_ChildExistRecordNotDeleted"));
                    return false;
                }
            }

            return true;
        }



    }
}
