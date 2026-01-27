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
            if (Env.IsModuleInstalled("VA137_"))
            {
                //Level-2 Team ID
                int level2TeamId = ResolveLevel2TeamId(parentTeamId, teamLevel);
                int count = DB.ExecuteQuery("UPDATE C_TEAM SET VA137_LEVEL2TEAM_ID=" + level2TeamId + " WHERE C_Team_ID=" + GetC_Team_ID(), null, null);
            }
            return true;
        }
        /// <summary>
        /// This function is used to generate the team code
        /// </summary>
        /// <param name="parentTeamId"></param>
        /// <param name="teamType"></param>
        /// <param name="teamLevel"></param>
        /// <returns>Team Code</returns>
        /// <author>VIS_427</author>
        private string GenerateTeamCode(int parentTeamId, string teamType, int teamLevel)
        {
            string sql = "";

            // ROOT TEAM (Level 1)
            if (parentTeamId <= 0 && teamLevel == 1)
            {
                sql = @"
            SELECT
                   VA137_TEAMTYPE
                   || '-' ||
                   TO_CHAR(COUNT(*) + 1)
            FROM C_TEAM
            WHERE VA137_TEAMTYPE = '" + teamType + @"'
              AND VA137_TEAM_ID IS NULL
            GROUP BY VA137_TEAMTYPE";
            }
            // CHILD TEAM (Level >= 2)
            else
            {
                sql = @"
            SELECT
                   p.VALUE
                   || '-' ||
                   TO_CHAR(NVL(c.cnt, 0) + 1)
            FROM C_TEAM p
            LEFT JOIN (
                    SELECT
                           VA137_TEAM_ID,
                           COUNT(*) cnt
                    FROM C_TEAM
                    WHERE VA137_TEAMTYPE = '" + teamType + @"'
                    GROUP BY VA137_TEAM_ID
            ) c
            ON c.VA137_TEAM_ID = p.C_TEAM_ID
            WHERE p.C_TEAM_ID = " + parentTeamId;
            }

            return Util.GetValueOfString(DB.ExecuteScalar(sql));
        }
        /// <summary>
        /// This function used to set team id of level2 team
        /// </summary>
        /// <param name="parentTeamId"></param>
        /// <param name="teamLevel"></param>
        /// <returns>Level 2 Team ID</returns>
        /// <author>VIS_427</author>
        private int ResolveLevel2TeamId(int parentTeamId, int teamLevel)
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
        WHERE C_TEAM_ID = " + parentTeamId));

            string[] parts = parentCode.Split('-');

            if (parts.Length < 3)
                return 0;

            string level2Code = parts[0] + "-" + parts[1] + "-" + parts[2];

            return Util.GetValueOfInt(DB.ExecuteScalar(@"
        SELECT C_TEAM_ID
        FROM C_TEAM
        WHERE VALUE = '" + level2Code + @"'"));
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

                int teamId = GetC_Team_ID();
                 parentTeamId = GetVA137_Team_ID();          // selected parent
                int oldParentTeamId = Util.GetValueOfInt(Get_ValueOld("VA137_Team_ID"));

                int pickLineId = GetVA137_PickListLine_ID();
                string teamType = GetVA137_TeamType();

                /* ================= TEAM LEVEL ================= */
                 teamLevel = Util.GetValueOfInt(DB.ExecuteScalar(@"
        SELECT VA137_TEAMLEVEL
        FROM VA137_PickListLine
        WHERE VA137_PickListLine_ID = " + pickLineId));

                /* ================= PARENT CHANGE VALIDATION ================= */
                if (!newRecord && parentTeamId != oldParentTeamId)
                {
                    int childCount = Util.GetValueOfInt(DB.ExecuteScalar(@"
            SELECT COUNT(*)
            FROM C_TEAM
            WHERE VA137_TEAM_ID = " + teamId));

                    if (childCount > 0)
                    {
                        log.SaveError(
                            "Error",
                            "Parent cannot be changed because this team already has child teams"
                        );
                        return false;
                    }
                }

                //Team Code
                string teamCode = GenerateTeamCode(parentTeamId, teamType, teamLevel);
                SetValue(teamCode);
            }

            return true;
        }


    }
}
