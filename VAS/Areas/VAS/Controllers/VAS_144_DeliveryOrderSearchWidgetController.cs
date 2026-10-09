using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Web.Mvc;
using VAdvantage.DataBase;
using VAdvantage.Model;
using VAdvantage.Utility;
using VIS.Filters;

namespace VAS.Controllers
{
    /// <summary>
    /// Module Name : VAS_144_DeliveryOrderSearchWidget (Delivery Order dashboard)
    /// Purpose     : Data endpoints for the 9x1 "Delivery Order Search" widget -
    ///               a type-ahead search over outbound customer delivery orders
    ///               (M_InOut, IsSOTrx='Y', MovementType='C-', returns excluded,
    ///               DocStatus DR/IP/WC/CO only) across eight facets in fixed
    ///               priority (DO number, customer, contact, contact phone,
    ///               ship-to location, sales order, representative, line item),
    ///               capped at 7 results, plus a full detail payload (header,
    ///               lines with product images, subtotal/tax/total honouring
    ///               C_Order.IsTaxIncluded and C_Tax.Rate) for the drill-down
    ///               modal. Amounts are derived from the DO's own lines
    ///               (qty x C_OrderLine.PriceActual) - never C_Order totals.
    ///               MRole is applied to the primary fetched table (M_InOut);
    ///               all input is parameterized; the SQL uses COALESCE / CASE /
    ///               EXISTS / ANSI joins / CTEs / FETCH FIRST only, so it runs
    ///               on Oracle 12c+ and PostgreSQL. Character literals that mix
    ///               with NVARCHAR columns go through NLiteral() (Oracle needs
    ///               the N'' prefix - ORA-12704 - while PostgreSQL has no such
    ///               syntax).
    /// Widget size : 9 columns x 1 row.
    /// Widget number 144.
    /// Chronological development:
    ///   &lt;EmpCode&gt;   2026-07-18 Created
    /// </summary>
    public class VAS_144_DeliveryOrderSearchWidgetController : Controller
    {
        private const int MAX_RESULTS = 7;

        /// <summary>DB-appropriate character literal (Oracle N'..', PostgreSQL '..').</summary>
        private static string NLiteral(string text)
        {
            return DB.IsPostgreSQL() ? "'" + text + "'" : "N'" + text + "'";
        }

        /// <summary>Collapses repeated whitespace left by the SQL address assembly.</summary>
        private static string NormalizeSpaces(string value)
        {
            if (string.IsNullOrEmpty(value)) { return value; }
            return Regex.Replace(value, @"\s+", " ").Trim();
        }

        /// <summary>
        /// Type-ahead search: at most 7 delivery orders matching the trimmed
        /// text on any of the eight facets, ranked by the facet priority and
        /// then newest delivery date. Empty input returns an empty list without
        /// touching the database.
        /// </summary>
        /// <param name="q">Search text (substring, case-insensitive).</param>
        /// <returns>JSON { rows[] }.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult SearchDeliveryOrders(string q = "")
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            string trimmed = (q ?? "").Trim();
            if (trimmed.Length == 0)
            {
                return Ok(new { rows = new List<object>() });
            }

            string like = "%" + trimmed.ToLowerInvariant() + "%";
            string E = NLiteral("");   // empty-string literal
            string S = NLiteral(" ");  // single-space literal

            // Secured base set: the access SQL is applied to this fragment only,
            // so MRole never sees the facet subqueries' aliases (ORA-00904 guard).
            string doBase = @"
                SELECT io.M_InOut_ID AS do_id,
                       io.DocumentNo AS do_number,
                       io.DocStatus AS status_code,
                       io.MovementDate AS delivery_date,
                       bp.Name AS customer_name,
                       contact_user.Name AS contact_name,
                       COALESCE(contact_user.Mobile, contact_user.Phone, bpl.Phone, bpl.Phone2) AS contact_phone,
                       TRIM(COALESCE(loc.Address1," + E + @") || " + S + @" || COALESCE(loc.Address2," + E + @") || " + S + @" ||
                            COALESCE(loc.Address3," + E + @") || " + S + @" || COALESCE(loc.Address4," + E + @") || " + S + @" ||
                            COALESCE(city.Name, loc.City," + E + @") || " + S + @" || COALESCE(region.Name, loc.RegionName," + E + @") || " + S + @" ||
                            COALESCE(country.Name," + E + @") || " + S + @" || COALESCE(loc.Postal," + E + @")) AS location_text,
                       so.DocumentNo AS sales_order_no,
                       rep.Name AS representative_name
                FROM M_InOut io
                INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID = io.C_BPartner_ID)
                LEFT JOIN AD_User contact_user ON (contact_user.AD_User_ID = io.AD_User_ID)
                LEFT JOIN C_BPartner_Location bpl ON (bpl.C_BPartner_Location_ID = io.C_BPartner_Location_ID)
                LEFT JOIN C_Location loc ON (loc.C_Location_ID = bpl.C_Location_ID)
                LEFT JOIN C_City city ON (city.C_City_ID = loc.C_City_ID)
                LEFT JOIN C_Region region ON (region.C_Region_ID = loc.C_Region_ID)
                LEFT JOIN C_Country country ON (country.C_Country_ID = loc.C_Country_ID)
                LEFT JOIN C_Order so ON (so.C_Order_ID = io.C_Order_ID)
                LEFT JOIN AD_User rep ON (rep.AD_User_ID = io.SalesRep_ID)
                WHERE io.IsActive = 'Y'
                  AND io.IsSOTrx = 'Y'
                  AND COALESCE(io.IsReturnTrx, 'N') = 'N'
                  AND io.MovementType = 'C-'
                  AND io.DocStatus IN ('DR','IP','WC','CO')
                  AND io.AD_Client_ID = @AD_Client_ID";

            doBase = MRole.GetDefault(ctx).AddAccessSQL(
                doBase,
                "io",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            // Facet flags are computed once per row; the outer SELECT derives
            // the priority / facet code / matched value from the flags, so each
            // LIKE parameter appears exactly once (@Q1..@Q11 in order - the DB
            // layer binds positionally).
            string sql = @"
                WITH do_base AS (
                    " + doBase + @"
                ),
                matched AS (
                    SELECT b.do_id, b.do_number, b.customer_name, b.contact_name, b.contact_phone,
                           b.location_text, b.sales_order_no, b.representative_name, b.status_code, b.delivery_date,
                           CASE WHEN LOWER(COALESCE(b.do_number," + E + @")) LIKE @Q1 THEN 1 ELSE 0 END AS m_do,
                           CASE WHEN LOWER(COALESCE(b.customer_name," + E + @")) LIKE @Q2 THEN 1 ELSE 0 END AS m_cust,
                           CASE WHEN LOWER(COALESCE(b.contact_name," + E + @")) LIKE @Q3 THEN 1 ELSE 0 END AS m_contact,
                           CASE WHEN LOWER(COALESCE(b.contact_phone," + E + @")) LIKE @Q4 THEN 1 ELSE 0 END AS m_phone,
                           CASE WHEN LOWER(COALESCE(b.location_text," + E + @")) LIKE @Q5 THEN 1 ELSE 0 END AS m_loc,
                           CASE WHEN LOWER(COALESCE(b.sales_order_no," + E + @")) LIKE @Q6 THEN 1 ELSE 0 END AS m_so,
                           CASE WHEN LOWER(COALESCE(b.representative_name," + E + @")) LIKE @Q7 THEN 1 ELSE 0 END AS m_rep,
                           CASE WHEN EXISTS (
                               SELECT 1
                               FROM M_InOutLine iol_search
                               INNER JOIN M_Product p_search ON (p_search.M_Product_ID = iol_search.M_Product_ID)
                               WHERE iol_search.M_InOut_ID = b.do_id
                                 AND iol_search.IsActive = 'Y'
                                 AND (LOWER(COALESCE(p_search.Name," + E + @")) LIKE @Q8
                                      OR LOWER(COALESCE(p_search.Value," + E + @")) LIKE @Q9)
                           ) THEN 1 ELSE 0 END AS m_line,
                           (SELECT MIN(TRIM(COALESCE(p2.Name," + E + @") || " + S + @" || COALESCE(p2.Value," + E + @")))
                            FROM M_InOutLine iol2
                            INNER JOIN M_Product p2 ON (p2.M_Product_ID = iol2.M_Product_ID)
                            WHERE iol2.M_InOut_ID = b.do_id
                              AND iol2.IsActive = 'Y'
                              AND (LOWER(COALESCE(p2.Name," + E + @")) LIKE @Q10
                                   OR LOWER(COALESCE(p2.Value," + E + @")) LIKE @Q11)) AS line_match_value
                    FROM do_base b
                )
                SELECT ranked.do_id, ranked.do_number, ranked.customer_name, ranked.status_code,
                       ranked.sales_order_no, ranked.location_text, ranked.matched_facet, ranked.matched_value
                FROM (
                    SELECT m.do_id, m.do_number, m.customer_name, m.status_code, m.sales_order_no,
                           m.location_text, m.delivery_date,
                           CASE WHEN m.m_do=1 THEN 1 WHEN m.m_cust=1 THEN 2 WHEN m.m_contact=1 THEN 3
                                WHEN m.m_phone=1 THEN 4 WHEN m.m_loc=1 THEN 5 WHEN m.m_so=1 THEN 6
                                WHEN m.m_rep=1 THEN 7 WHEN m.m_line=1 THEN 8 END AS matched_priority,
                           CASE WHEN m.m_do=1 THEN " + NLiteral("DO") + @" WHEN m.m_cust=1 THEN " + NLiteral("CUST") + @"
                                WHEN m.m_contact=1 THEN " + NLiteral("CONTACT") + @" WHEN m.m_phone=1 THEN " + NLiteral("CONTACT") + @"
                                WHEN m.m_loc=1 THEN " + NLiteral("LOC") + @" WHEN m.m_so=1 THEN " + NLiteral("SO") + @"
                                WHEN m.m_rep=1 THEN " + NLiteral("REP") + @" WHEN m.m_line=1 THEN " + NLiteral("LINE") + @" END AS matched_facet,
                           CASE WHEN m.m_do=1 THEN m.do_number WHEN m.m_cust=1 THEN m.customer_name
                                WHEN m.m_contact=1 THEN m.contact_name WHEN m.m_phone=1 THEN m.contact_phone
                                WHEN m.m_loc=1 THEN m.location_text WHEN m.m_so=1 THEN m.sales_order_no
                                WHEN m.m_rep=1 THEN m.representative_name ELSE m.line_match_value END AS matched_value
                    FROM matched m
                    WHERE m.m_do=1 OR m.m_cust=1 OR m.m_contact=1 OR m.m_phone=1
                       OR m.m_loc=1 OR m.m_so=1 OR m.m_rep=1 OR m.m_line=1
                ) ranked
                ORDER BY ranked.matched_priority, ranked.delivery_date DESC, ranked.do_number DESC
                FETCH FIRST " + MAX_RESULTS + @" ROWS ONLY";

            List<SqlParameter> parameters = new List<SqlParameter>();
            parameters.Add(new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID()));
            for (int i = 1; i <= 11; i++)
            {
                parameters.Add(new SqlParameter("@Q" + i, like));
            }

            List<object> rows = new List<object>();
            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql, parameters.ToArray());

                while (dr != null && dr.Read())
                {
                    rows.Add(new
                    {
                        doId = Util.GetValueOfInt(dr["do_id"]),
                        doNumber = Util.GetValueOfString(dr["do_number"]),
                        customer = Util.GetValueOfString(dr["customer_name"]),
                        statusCode = Util.GetValueOfString(dr["status_code"]),
                        salesOrder = Util.GetValueOfString(dr["sales_order_no"]),
                        location = NormalizeSpaces(Util.GetValueOfString(dr["location_text"])),
                        matchedFacet = Util.GetValueOfString(dr["matched_facet"]),
                        matchedValue = NormalizeSpaces(Util.GetValueOfString(dr["matched_value"]))
                    });
                }

                return Ok(new { rows = rows });
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }
        }

        /// <summary>
        /// Full detail of one delivery order for the modal: header (customer,
        /// contact, location, sales order, representative, warehouse, carrier
        /// pieces, date, notes), currency (from the linked sales order, session
        /// currency as fallback), the active lines in line order with resolved
        /// product images, and subtotal/tax/total computed over ALL lines per
        /// the tax-included rule. An inaccessible or non-qualifying id returns
        /// an empty payload (doId 0), never an error with internals.
        /// </summary>
        /// <param name="doId">M_InOut_ID of the delivery order.</param>
        /// <returns>JSON detail contract.</returns>
        [AjaxAuthorizeAttribute]
        [AjaxSessionFilterAttribute]
        public JsonResult GetDeliveryOrder(int doId = 0)
        {
            if (Session["ctx"] == null)
            {
                return Fail(Msg.GetMsg(Env.GetCtx(), "SessionExpired") ?? "Session Expired");
            }

            Ctx ctx = Session["ctx"] as Ctx;

            if (doId <= 0)
            {
                return Ok(new { doId = 0, lines = new List<object>() });
            }

            string E = NLiteral("");
            string S = NLiteral(" ");

            // One query, header repeated per line; grouped in C# below (no
            // DB-specific JSON/string aggregation).
            string sql = @"
                SELECT io.M_InOut_ID AS do_id,
                       io.DocumentNo AS do_number,
                       io.DocStatus AS status_code,
                       io.MovementDate AS delivery_date,
                       io.Description AS delivery_notes,
                       bp.Name AS customer_name,
                       contact_user.Name AS contact_name,
                       COALESCE(contact_user.Mobile, contact_user.Phone, bpl.Phone, bpl.Phone2) AS contact_phone,
                       TRIM(COALESCE(loc.Address1," + E + @") || " + S + @" || COALESCE(loc.Address2," + E + @") || " + S + @" ||
                            COALESCE(loc.Address3," + E + @") || " + S + @" || COALESCE(loc.Address4," + E + @") || " + S + @" ||
                            COALESCE(city.Name, loc.City," + E + @") || " + S + @" || COALESCE(region.Name, loc.RegionName," + E + @") || " + S + @" ||
                            COALESCE(country.Name," + E + @") || " + S + @" || COALESCE(loc.Postal," + E + @")) AS location_text,
                       so.DocumentNo AS sales_order_no,
                       rep.Name AS representative_name,
                       wh.Name AS warehouse_name,
                       shipper.Name AS shipper_name,
                       io.DeliveryViaRule AS delivery_via_rule,
                       io.VAS_VehicleName AS vehicle_name,
                       io.VAS_VehicleRegistrationNo AS vehicle_registration_no,
                       currency.ISO_Code AS currency_code,
                       currency.StdPrecision AS currency_precision,
                       so.IsTaxIncluded AS is_tax_included,
                       iol.M_InOutLine_ID AS line_id,
                       iol.Line AS line_no,
                       product.Name AS product_name,
                       product.Value AS sku,
                       COALESCE(iol.QtyEntered, iol.MovementQty) AS quantity,
                       order_line.PriceActual AS unit_price,
                       tax.Rate AS tax_rate,
                       product.ImageURL AS product_image_url,
                       image.ImageURL AS ad_image_url,
                       image.ImageExtension AS ad_image_extension,
                       product.AD_Image_ID AS product_image_id
                FROM M_InOut io
                INNER JOIN C_BPartner bp ON (bp.C_BPartner_ID = io.C_BPartner_ID)
                LEFT JOIN AD_User contact_user ON (contact_user.AD_User_ID = io.AD_User_ID)
                LEFT JOIN C_BPartner_Location bpl ON (bpl.C_BPartner_Location_ID = io.C_BPartner_Location_ID)
                LEFT JOIN C_Location loc ON (loc.C_Location_ID = bpl.C_Location_ID)
                LEFT JOIN C_City city ON (city.C_City_ID = loc.C_City_ID)
                LEFT JOIN C_Region region ON (region.C_Region_ID = loc.C_Region_ID)
                LEFT JOIN C_Country country ON (country.C_Country_ID = loc.C_Country_ID)
                LEFT JOIN C_Order so ON (so.C_Order_ID = io.C_Order_ID)
                LEFT JOIN C_Currency currency ON (currency.C_Currency_ID = so.C_Currency_ID)
                LEFT JOIN AD_User rep ON (rep.AD_User_ID = io.SalesRep_ID)
                LEFT JOIN M_Warehouse wh ON (wh.M_Warehouse_ID = io.M_Warehouse_ID)
                LEFT JOIN M_Shipper shipper ON (shipper.M_Shipper_ID = io.M_Shipper_ID)
                LEFT JOIN M_InOutLine iol ON (iol.M_InOut_ID = io.M_InOut_ID AND iol.IsActive = 'Y')
                LEFT JOIN M_Product product ON (product.M_Product_ID = iol.M_Product_ID)
                LEFT JOIN AD_Image image ON (image.AD_Image_ID = product.AD_Image_ID)
                LEFT JOIN C_OrderLine order_line ON (order_line.C_OrderLine_ID = iol.C_OrderLine_ID)
                LEFT JOIN C_Tax tax ON (tax.C_Tax_ID = order_line.C_Tax_ID)
                WHERE io.M_InOut_ID = @DO_ID
                  AND io.IsActive = 'Y'
                  AND io.IsSOTrx = 'Y'
                  AND COALESCE(io.IsReturnTrx, 'N') = 'N'
                  AND io.MovementType = 'C-'
                  AND io.DocStatus IN ('DR','IP','WC','CO')
                  AND io.AD_Client_ID = @AD_Client_ID";

            sql = MRole.GetDefault(ctx).AddAccessSQL(
                sql,
                "io",
                MRole.SQL_FULLYQUALIFIED,
                MRole.SQL_RO
            );

            sql += " ORDER BY iol.Line";

            object header = null;
            int headerDoId = 0;
            string doNumber = "", statusCode = "", customerName = "", contactName = "", contactPhone = "";
            string locationText = "", salesOrderNo = "", representativeName = "", warehouseName = "";
            string shipperName = "", deliveryViaRule = "", vehicleName = "", vehicleRegistrationNo = "";
            string deliveryNotes = "", currencyCode = "", isTaxIncluded = "N";
            int currencyPrecision = -1;
            DateTime? deliveryDate = null;

            List<object> lines = new List<object>();
            decimal subtotal = 0m, taxTotal = 0m, grandTotal = 0m;
            bool taxIncluded = false;
            IDataReader dr = null;

            try
            {
                dr = DB.ExecuteReader(sql, new SqlParameter[]
                {
                    new SqlParameter("@DO_ID", doId),
                    new SqlParameter("@AD_Client_ID", ctx.GetAD_Client_ID())
                });

                while (dr != null && dr.Read())
                {
                    if (headerDoId == 0)
                    {
                        headerDoId = Util.GetValueOfInt(dr["do_id"]);
                        doNumber = Util.GetValueOfString(dr["do_number"]);
                        statusCode = Util.GetValueOfString(dr["status_code"]);
                        deliveryDate = Util.GetValueOfDateTime(dr["delivery_date"]);
                        deliveryNotes = Util.GetValueOfString(dr["delivery_notes"]);
                        customerName = Util.GetValueOfString(dr["customer_name"]);
                        contactName = Util.GetValueOfString(dr["contact_name"]);
                        contactPhone = Util.GetValueOfString(dr["contact_phone"]);
                        locationText = NormalizeSpaces(Util.GetValueOfString(dr["location_text"]));
                        salesOrderNo = Util.GetValueOfString(dr["sales_order_no"]);
                        representativeName = Util.GetValueOfString(dr["representative_name"]);
                        warehouseName = Util.GetValueOfString(dr["warehouse_name"]);
                        shipperName = Util.GetValueOfString(dr["shipper_name"]);
                        deliveryViaRule = Util.GetValueOfString(dr["delivery_via_rule"]);
                        vehicleName = Util.GetValueOfString(dr["vehicle_name"]);
                        vehicleRegistrationNo = Util.GetValueOfString(dr["vehicle_registration_no"]);
                        currencyCode = Util.GetValueOfString(dr["currency_code"]);
                        currencyPrecision = dr["currency_precision"] == DBNull.Value ? -1 : Util.GetValueOfInt(dr["currency_precision"]);
                        isTaxIncluded = Util.GetValueOfString(dr["is_tax_included"]);
                        taxIncluded = "Y".Equals(isTaxIncluded, StringComparison.OrdinalIgnoreCase);
                    }

                    int lineId = Util.GetValueOfInt(dr["line_id"]);
                    if (lineId <= 0) { continue; } // header row of a DO without lines

                    decimal quantity = Util.GetValueOfDecimal(dr["quantity"]);
                    bool hasPrice = dr["unit_price"] != DBNull.Value;
                    decimal unitPrice = hasPrice ? Util.GetValueOfDecimal(dr["unit_price"]) : 0m;
                    decimal taxRate = dr["tax_rate"] == DBNull.Value ? 0m : Util.GetValueOfDecimal(dr["tax_rate"]);

                    // Line amount = actual DO quantity x linked SO line price.
                    // A line without an SO link has NO price (dash in the UI)
                    // and contributes nothing to the totals - never cost.
                    decimal? lineTotal = hasPrice ? (decimal?)(quantity * unitPrice) : null;
                    if (lineTotal.HasValue)
                    {
                        if (!taxIncluded)
                        {
                            subtotal += lineTotal.Value;
                            taxTotal += lineTotal.Value * taxRate / 100m;
                        }
                        else
                        {
                            grandTotal += lineTotal.Value;
                            taxTotal += lineTotal.Value - (lineTotal.Value / (1m + taxRate / 100m));
                        }
                    }

                    string imageUrl = Util.GetValueOfString(dr["product_image_url"]);
                    if (string.IsNullOrEmpty(imageUrl))
                    {
                        imageUrl = GetProductImageUrl(
                            ctx,
                            Util.GetValueOfInt(dr["product_image_id"]),
                            Util.GetValueOfString(dr["ad_image_extension"]),
                            Util.GetValueOfString(dr["ad_image_url"]));
                    }

                    lines.Add(new
                    {
                        lineId = lineId,
                        lineNo = Util.GetValueOfInt(dr["line_no"]),
                        productName = Util.GetValueOfString(dr["product_name"]),
                        sku = Util.GetValueOfString(dr["sku"]),
                        qty = quantity,
                        unitPrice = hasPrice ? (decimal?)unitPrice : null,
                        lineDisplayTotal = lineTotal,
                        taxRate = taxRate,
                        imageUrl = imageUrl
                    });
                }
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
            finally
            {
                if (dr != null) { dr.Close(); dr.Dispose(); }
            }

            if (headerDoId == 0)
            {
                // Inaccessible / non-qualifying record: empty result, no internals.
                return Ok(new { doId = 0, lines = new List<object>() });
            }

            // Session-currency fallback when the DO has no linked sales order.
            if (string.IsNullOrEmpty(currencyCode) || currencyPrecision < 0)
            {
                IDataReader curReader = null;
                try
                {
                    curReader = DB.ExecuteReader(
                        @"SELECT ISO_Code, StdPrecision FROM C_Currency WHERE C_Currency_ID=@Currency_ID",
                        new SqlParameter[] { new SqlParameter("@Currency_ID", ctx.GetContextAsInt("$C_Currency_ID")) });
                    if (curReader != null && curReader.Read())
                    {
                        if (string.IsNullOrEmpty(currencyCode)) { currencyCode = Util.GetValueOfString(curReader["ISO_Code"]); }
                        if (currencyPrecision < 0) { currencyPrecision = Util.GetValueOfInt(curReader["StdPrecision"]); }
                    }
                }
                finally
                {
                    if (curReader != null) { curReader.Close(); curReader.Dispose(); }
                }
            }
            if (currencyPrecision < 0) { currencyPrecision = 2; }

            // Tax-exclusive: Total = Subtotal + Tax. Tax-inclusive: the line
            // totals already carry tax, so Subtotal = Total - Tax.
            if (!taxIncluded) { grandTotal = subtotal + taxTotal; }
            else { subtotal = grandTotal - taxTotal; }

            subtotal = decimal.Round(subtotal, currencyPrecision, MidpointRounding.AwayFromZero);
            taxTotal = decimal.Round(taxTotal, currencyPrecision, MidpointRounding.AwayFromZero);
            grandTotal = decimal.Round(grandTotal, currencyPrecision, MidpointRounding.AwayFromZero);

            header = new
            {
                doId = headerDoId,
                doNumber = doNumber,
                statusCode = statusCode,
                customer = customerName,
                contactName = contactName,
                contactPhone = contactPhone,
                location = locationText,
                salesOrder = salesOrderNo,
                representative = representativeName,
                warehouse = warehouseName,
                // Carrier pieces: the widget composes the display with priority
                // shipper name -> vehicle (+ registration) -> translated
                // DeliveryViaRule label -> dash. Nothing is invented here.
                shipperName = shipperName,
                vehicleName = vehicleName,
                vehicleRegistrationNo = vehicleRegistrationNo,
                deliveryViaRule = deliveryViaRule,
                deliveryDate = deliveryDate.HasValue ? deliveryDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                notes = deliveryNotes,
                currencyCode = currencyCode,
                currencyPrecision = currencyPrecision,
                subtotal = subtotal,
                tax = taxTotal,
                total = grandTotal,
                lines = lines
            };

            return Ok(header);
        }

        /// <summary>
        /// Resolves a product's AD_Image record to something the browser can
        /// render - the same chain as VAS_094 / VAS_078: an absolute stored URL
        /// as-is, then an existing file under GlobalVariable.ImagePath (original
        /// first, then thumbnails), then the image BLOB as a data URI (via the
        /// MImage model - AD_Image.BinaryData is never selected in the widget
        /// query). Null keeps the client's package fallback tile.
        /// </summary>
        private string GetProductImageUrl(Ctx ctx, int adImageId, string imageExtension, string storedImageUrl)
        {
            if (ctx == null || adImageId <= 0) { return null; }

            if (!string.IsNullOrEmpty(storedImageUrl)
                && (storedImageUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || storedImageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    || storedImageUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase)))
            {
                return storedImageUrl;
            }

            string fileName = GetImageFileName(adImageId, imageExtension, storedImageUrl);
            if (!string.IsNullOrEmpty(fileName) && !string.IsNullOrEmpty(GlobalVariable.ImagePath))
            {
                string[] subFolders = { "", "Thumb140x120", "Thumb46x46", "Thumb320x240", "Thumb320x185", "Thumb500x375" };
                foreach (string subFolder in subFolders)
                {
                    string folder = subFolder.Length == 0
                        ? GlobalVariable.ImagePath
                        : System.IO.Path.Combine(GlobalVariable.ImagePath, subFolder);
                    if (System.IO.File.Exists(System.IO.Path.Combine(folder, fileName)))
                    {
                        return subFolder.Length == 0
                            ? "Images/" + fileName
                            : "Images/" + subFolder + "/" + fileName;
                    }
                }
            }

            MImage image = MImage.Get(ctx, adImageId);
            if (image != null)
            {
                byte[] data = image.GetBinaryData();
                if (data != null && data.Length > 0)
                {
                    return "data:" + GetImageMimeType(imageExtension, data) + ";base64," + Convert.ToBase64String(data);
                }
            }

            return null;
        }

        /// <summary>
        /// File name of the uploaded image: the name on AD_Image.ImageURL when
        /// set (stripped of folders so it can never escape the Images
        /// directory), else the upload convention &lt;AD_Image_ID&gt;&lt;ext&gt;.
        /// </summary>
        private string GetImageFileName(int adImageId, string imageExtension, string storedImageUrl)
        {
            if (!string.IsNullOrEmpty(storedImageUrl))
            {
                try
                {
                    string fileName = System.IO.Path.GetFileName(storedImageUrl.Trim());
                    if (!string.IsNullOrEmpty(fileName)) { return fileName; }
                }
                catch (ArgumentException)
                {
                    // Invalid path characters - fall through to the convention.
                }
            }
            return !string.IsNullOrEmpty(imageExtension) ? adImageId + imageExtension : null;
        }

        private string GetImageMimeType(string imageExtension, byte[] data)
        {
            // The stored extension is unreliable for BLOB images - sniff first.
            if (data != null && data.Length > 11)
            {
                if (data[0] == 0xFF && data[1] == 0xD8) { return "image/jpeg"; }
                if (data[0] == 0x89 && data[1] == 0x50) { return "image/png"; }
                if (data[0] == 0x47 && data[1] == 0x49) { return "image/gif"; }
                if (data[0] == 0x42 && data[1] == 0x4D) { return "image/bmp"; }
                if (data[0] == 0x52 && data[1] == 0x49 && data[8] == 0x57 && data[9] == 0x45) { return "image/webp"; }
            }

            switch ((imageExtension ?? "").Trim().ToLowerInvariant())
            {
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".bmp": return "image/bmp";
                case ".webp": return "image/webp";
                default: return "image/png";
            }
        }

        /// <summary>Wraps a success payload as a serialized JSON result.</summary>
        private JsonResult Ok(object result)
        {
            return Json(JsonConvert.SerializeObject(result), JsonRequestBehavior.AllowGet);
        }

        /// <summary>Wraps a failure message as a serialized JSON result.</summary>
        private JsonResult Fail(string message)
        {
            return Json(JsonConvert.SerializeObject(new
            {
                success = false,
                error = message,
                message = message
            }), JsonRequestBehavior.AllowGet);
        }
    }
}
