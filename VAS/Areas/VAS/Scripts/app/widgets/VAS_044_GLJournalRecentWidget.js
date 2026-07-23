/**
 * GL Journal Recent Entries Widget
 * Purpose  : Display the 6 most recent GL_Journal documents with document
 *            number, account date, description, status and total debit / credit.
 *            Clicking a row zooms to that document in the GL Journal window.
 * Tables   : GL_Journal, GL_JournalLine, AD_Ref_List, C_AcctSchema, C_Currency
 */


/*
 * ============================================================
 * VAS_044_GLJournalRecentWidget Messages
 * ============================================================
 *
 * Custom Messages:
 *
 * Value                                English Text                          Arabic Text
 * ---------------------------------------------------------------------------------------------------------
 * VAS_044_RecentJournalEntries         Recent Journal Entries               قيود اليومية الحديثة
 * VAS_044_Hash                         #                                    #
 * VAS_044_Date                         Date                                 التاريخ
 * VAS_044_Description                  Description                          الوصف
 * VAS_044_Status                       Status                               الحالة
 * VAS_044_Debit                        Debit                                مدين
 * VAS_044_Credit                       Credit                               دائن
 * VAS_044_JournalNo                    Journal No.                          رقم القيد
 * VAS_044_AccountingBook               Accounting Book                      الدفتر المحاسبي
 * VAS_044_TotalDebit                   Total Debit                          إجمالي المدين
 * VAS_044_TotalCredit                  Total Credit                         إجمالي الدائن
 * VAS_044_JournalLines                 Journal Lines                        س�\u00B7ور القيد
 * VAS_044_Account                      Account                              الحساب
 * VAS_044_CostCenter                   Cost Center                          مركز التكلفة
 * VAS_044_BusinessPartner              Business Partner                     شريك العمل
 * VAS_044_Product                      Product                              المنتج
 * VAS_044_Project                      Project                              المشروع
 * VAS_044_Total                        Total                                الإجمالي
 * VAS_044_CreatedBy                    Created By                           تم الإنشاء بواس�\u00B7ة
 * VAS_044_Drafted                      Drafted                              تم إنشاء المسودة
 * VAS_044_NoJournalLines               No journal lines.                    لا توجد س�\u00B7ور للقيد
 * VAS_044_Approving                    Approving...                         جارٍ تنفيذ الموافقة...
 * VAS_044_Posting                      Posting...                           جارٍ ترحيل القيد...
 * VAS_044_JournalProcessFailed         Journal process failed.              فشلت معالجة القيد
 * VAS_044_DetailsNotLoaded             Journal details are not loaded.      لم يتم تحميل تفاصيل القيد
 * VAS_044_DetailsNotAvailable          Journal details are not available.   تفاصيل القيد غير متوفرة
 * VAS_044_PrintWindowFailed            Could not open the print window.     تعذر فتح نافذة ال�\u00B7باعة
 * VAS_044_LoadFailed                   Could Not Load Recent Entries        
 * VAS_044_InvalidJournalID             Invalid Journal ID                   
 * VAS_044_DetailsLoadFailed            Could Not Load Journal Details       
 *
 * Shared Messages:
 *
 * Value                                English Text                          Arabic Text
 * ---------------------------------------------------------------------------------------------------------
 * VAS_DownloadPDF                      Download PDF                         تنزيل PDF
 * VAS_044_Approve                      Approve                              موافقة
 * VAS_044_PostJournal                  Post journal                         ترحيل القيد
 * VAS_Close                            Close                                إغلاق
 *
 * Existing VIS Messages:
 *
 * Value                                English Text
 * ---------------------------------------------------------------------------------------------------------
 * VIS_Previous                         Previous
 * VIS_Next                             Next
 * VIS_Of                               of
 * VIS_NoData                           No data available.
 * VIS_Error                            Error loading data.
 *
 * ============================================================
 */


/**
 * GL Journal Recent Entries Widget
 *
 * Purpose:
 * Displays the most recent GL journal entries and opens
 * the selected journal details inside a popup.
 *
 * Status response:
 *
 * Status: {
 *     Value: "DR",
 *     Name: "Drafted"
 * }
 */

; VAS = window.VAS || {};

; (function (VAS, $) {
    /* Creates a single document-level ResizeObserver on the dashboard container
       and mirrors its width into the global CSS var --dash-inline-size (px), so
       the widget's clamp() sizing tracks the dashboard width, not the viewport. */
    function ensureDashInlineSizeVar($el) {
        if (window.__vasDashInlineSizeObserver) { return; }
        if (typeof ResizeObserver === 'undefined') { return; }

        var container = $el.closest('.vis-widget-container, [data-dashboard-container]')[0];
        if (!container) { return; }

        var write = function () {
            document.documentElement.style.setProperty('--dash-inline-size', container.clientWidth + 'px');
        };

        window.__vasDashInlineSizeObserver = new ResizeObserver(write);
        window.__vasDashInlineSizeObserver.observe(container);
        write();
    }

    function lbl(key, fallback) {
        var text =
            VIS.Msg.getMsg(key);

        return (
            text &&
            text !== "[" + key + "]"
        )
            ? text
            : fallback;
    }

    function esc(value) {
        return String(
            value == null
                ? ""
                : value
        )
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#039;");
    }

    function normalizeResponse(result) {
        var data =
            result;

        if (
            data &&
            data.d !== undefined
        ) {
            data =
                data.d;
        }

        for (
            var index = 0;
            index < 3;
            index++
        ) {
            if (
                typeof data !== "string"
            ) {
                break;
            }

            try {
                data =
                    JSON.parse(data);
            }
            catch (error) {
                return null;
            }

            if (
                data &&
                data.d !== undefined
            ) {
                data =
                    data.d;
            }
        }

        return data;
    }

    /* Money label with the sign OUTSIDE the currency symbol — "-$220.00", not
       "$-220.00": the symbol belongs to the number, the minus to the value. The
       amount is formatted from its absolute value so toLocaleString can't
       reintroduce its own minus after the symbol. */
    function formatMoney(symbol, amount, precision) {
        var numericAmount = Number(amount || 0);

        if (isNaN(numericAmount)) {
            numericAmount = 0;
        }

        return (
            (numericAmount < 0 ? "-" : "") +
            (symbol || "") +
            formatAmount(Math.abs(numericAmount), precision)
        );
    }

    function formatAmount(
        amount,
        precision
    ) {
        var standardPrecision =
            2;

        try {
            standardPrecision =
                Number(
                    VIS.Env
                        .getCtx()
                        .getStdPrecision()
                );
        }
        catch (error) {
            standardPrecision =
                2;
        }

        var resolvedPrecision =
            Number(precision);

        if (
            isNaN(resolvedPrecision) ||
            resolvedPrecision < 0
        ) {
            resolvedPrecision =
                standardPrecision;
        }

        if (
            isNaN(resolvedPrecision) ||
            resolvedPrecision < 0
        ) {
            resolvedPrecision =
                2;
        }

        var numericAmount =
            Number(
                amount || 0
            );

        if (isNaN(numericAmount)) {
            numericAmount =
                0;
        }

        return numericAmount.toLocaleString(
            window.navigator.language,
            {
                minimumFractionDigits:
                    resolvedPrecision,

                maximumFractionDigits:
                    resolvedPrecision
            }
        );
    }

    function resolveTotalCount(totalCount, visibleCount, pageSize, totalPages) {
        var total =
            Number(
                totalCount || 0
            );

        if (
            !isNaN(total) &&
            total > 0
        ) {
            return total;
        }

        var rows =
            Number(
                visibleCount || 0
            );

        if (
            isNaN(rows) ||
            rows <= 0
        ) {
            return 0;
        }

        var size =
            Math.max(
                parseInt(
                    pageSize || rows || 1,
                    10
                ),
                1
            );

        var pages =
            Math.max(
                parseInt(
                    totalPages || 1,
                    10
                ),
                1
            );

        if (pages > 1) {
            return Math.max(
                ((pages - 1) * size) + rows,
                rows
            );
        }

        return rows;
    }

    function formatRangeText(pageNo, pageSize, totalCount) {
        var total =
            Number(
                totalCount || 0
            );

        if (
            isNaN(total) ||
            total <= 0
        ) {
            return "";
        }

        var page =
            Math.max(
                parseInt(
                    pageNo || 1,
                    10
                ),
                1
            );

        var size =
            Math.max(
                parseInt(
                    pageSize || total,
                    10
                ),
                1
            );

        var start =
            ((page - 1) * size) + 1;

        if (start > total) {
            start =
                total;
        }

        var end =
            Math.min(
                start + size - 1,
                total
            );

        return (
            "Showing " +
            start +
            "-" +
            end +
            " of " +
            total
        );
    }

    function isPosted(value) {
        if (
            value === true ||
            value === 1
        ) {
            return true;
        }

        var text =
            String(
                value == null
                    ? ""
                    : value
            ).toUpperCase();

        return (
            text === "Y" ||
            text === "TRUE" ||
            text === "1"
        );
    }

    /*
     * Supports the new Status object and the old fields.
     */
    function getStatusValue(source) {
        source =
            source || {};

        if (
            source.Status &&
            source.Status.Value != null
        ) {
            return String(
                source.Status.Value
            );
        }

        return String(
            source.StatusValue ||
            source.DocStatus ||
            ""
        );
    }

    function getStatusName(source) {
        source =
            source || {};

        if (
            source.Status &&
            source.Status.Name
        ) {
            return String(
                source.Status.Name
            );
        }

        return String(
            source.StatusName ||
            getStatusValue(source) ||
            ""
        );
    }

    function getAjaxErrorMessage(
        xhr,
        fallback
    ) {
        var response =
            normalizeResponse(
                xhr &&
                xhr.responseText
            );

        if (response) {
            if (response.errorKey || response.messageKey) {
                return lbl(
                    response.errorKey || response.messageKey,
                    response.errorText ||
                    response.error ||
                    response.message ||
                    fallback ||
                    lbl(
                        "VIS_Error",
                        "Error loading data."
                    )
                );
            }

            return (
                response.errorText ||
                response.error ||
                response.message ||
                fallback ||
                lbl(
                    "VIS_Error",
                    "Error loading data."
                )
            );
        }

        if (
            xhr &&
            xhr.status
        ) {
            return (
                lbl(
                    "VAS_044_RequestFailedHttp",
                    "Request failed. HTTP {0}"
                )
                    .replace(
                        "{0}",
                        xhr.status
                    )
            );
        }

        return (
            fallback ||
            lbl(
                "VIS_Error",
                "Error loading data."
            )
        );
    }

    var PILL_CLASS = {
        "DR":
            "VAS-gljr-pill-draft",

        "CO":
            "VAS-gljr-pill-posted",

        "IP":
            "VAS-gljr-pill-submit",

        "AP":
            "VAS-gljr-pill-posted",

        "NA":
            "VAS-gljr-pill-pending",

        "VO":
            "VAS-gljr-pill-voided",

        "RE":
            "VAS-gljr-pill-reverse",

        "CL":
            "VAS-gljr-pill-closed"
    };

    VAS.VAS_044_GLJournalRecentWidget =
        function () {
            this.frame =
                null;

            this.windowNo =
                0;

            this.AD_UserHomeWidgetID =
                0;

            var $self =
                this;

            var $root =
                $(
                    '<div class="VAS-gljr-root">'
                );

            var currentData =
                null;

            var pageNo =
                1;

            var pageSize =
                4;                       // initial guess; replaced by the adaptive measure

            var totalPages =
                0;

            var totalCount =
                0;

            /* Adaptive page size (VAS_020 pattern): derive the visible row count from the
               table's available height at runtime so rows always fit the card (no clipped
               last row); a ResizeObserver re-pages on resize. Measure only on the first
               paint (via _needsSync) and on resize (via the observer) — never on nav. */
            var _rowH = 0;               // last measured body-row height (px)
            var _needsSync = true;       // measure capacity on first paint only
            var _listObserver = null;
            var LIST_MIN_ROWS = 1;       // never force more rows than physically fit
            var LIST_ROW_FALLBACK = 40;  // px, used only before a real row is measured

            var loadRequest =
                null;

            var isDisposed =
                false;

            var baseUrl =
                VIS.Application.contextUrl;

            this.Initalize =
                function () {
                    createWidget();
                    createBusyIndicator();

                    showBusy(true);

                    loadData();
                };

            function documentIconSvg() {
                return (
                    '<svg viewBox="0 0 24 24" ' +
                    'fill="none" ' +
                    'stroke="currentColor" ' +
                    'stroke-width="2" ' +
                    'stroke-linecap="round" ' +
                    'stroke-linejoin="round">' +

                    '<path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z"></path>' +

                    '<polyline points="14 2 14 8 20 8"></polyline>' +

                    "</svg>"
                );
            }

            function createBusyIndicator() {
                var $busy =
                    $(
                        '<div id="VAS-gljr-busy-' +
                        $self.AD_UserHomeWidgetID +
                        '" class="vis-busyindicatorouterwrap">' +

                        '<div class="vis-busyindicatorinnerwrap">' +

                        '<i class="vis_widgetloader"></i>' +

                        "</div>" +

                        "</div>"
                    );

                $root.append(
                    $busy
                );
            }

            function showBusy(show) {
                var $busy =
                    $root.find(
                        "#VAS-gljr-busy-" +
                        $self.AD_UserHomeWidgetID
                    );

                if (show) {
                    $busy.show();
                }
                else {
                    $busy.hide();
                }
            }

            function createWidget() {
                var id =
                    $self.AD_UserHomeWidgetID;

                var documentIcon =
                    documentIconSvg();

                var html =
                    '<div class="VAS-gljr-card">' +

                    '<div class="w-head">' +

                    '<div class="VAS-gljr-icon">' +
                    documentIcon +
                    "</div>" +

                    '<div class="VAS-gljr-title-wrap">' +

                    '<div class="w-title">' +

                    esc(
                        lbl(
                            "VAS_044_RecentJournalEntries",
                            "Recent Journal Entries"
                        )
                    ) +

                    "</div>" +

                    '<div class="VAS-gljr-subtitle">' +

                    esc(
                        lbl(
                            "VAS_044_RecentSubtitle",
                            "Records Created in Last 15 Days"
                        )
                    ) +

                    "</div>" +

                    "</div>" +

                    "</div>" +

                    '<div class="VAS-gljr-table-wrap">' +

                    '<table class="VAS-gljr-table">' +

                    "<thead>" +

                    "<tr>" +

                    "<th>" +
                    esc(
                        lbl(
                            "VAS_044_Hash",
                            "#"
                        )
                    ) +
                    "</th>" +

                    "<th>" +
                    esc(
                        lbl(
                            "VAS_044_Date",
                            "Date"
                        )
                    ) +
                    "</th>" +

                    "<th>" +
                    esc(
                        lbl(
                            "VAS_044_Description",
                            "Description"
                        )
                    ) +
                    "</th>" +

                    "<th>" +
                    esc(
                        lbl(
                            "VAS_044_Status",
                            "Status"
                        )
                    ) +
                    "</th>" +

                    '<th class="VAS-gljr-num">' +
                    esc(
                        lbl(
                            "VAS_044_Debit",
                            "Debit"
                        )
                    ) +
                    "</th>" +

                    '<th class="VAS-gljr-num">' +
                    esc(
                        lbl(
                            "VAS_044_Credit",
                            "Credit"
                        )
                    ) +
                    "</th>" +

                    "</tr>" +

                    "</thead>" +

                    '<tbody id="VAS-gljr-tbody-' +
                    id +
                    '">' +

                    '<tr>' +

                    '<td colspan="6" class="VAS-gljr-empty">' +

                    "&mdash;" +

                    "</td>" +

                    "</tr>" +

                    "</tbody>" +

                    "</table>" +

                    "</div>" +

                    '<div class="VAS-gljr-pager">' +

                    '<span class="VAS-gljr-page-text"></span>' +

                    '<button type="button" ' +

                    'class="VAS-gljr-page-btn VAS-gljr-prev" ' +

                    'aria-label="' +

                    esc(
                        lbl(
                            "VIS_Previous",
                            "Previous"
                        )
                    ) +

                    '">' +

                    "&#8249;" +

                    "</button>" +

                    '<span class="VAS-gljr-page-count"></span>' +

                    '<button type="button" ' +

                    'class="VAS-gljr-page-btn VAS-gljr-next" ' +

                    'aria-label="' +

                    esc(
                        lbl(
                            "VIS_Next",
                            "Next"
                        )
                    ) +

                    '">' +

                    "&#8250;" +

                    "</button>" +

                    "</div>" +

                    "</div>";

                $root.append(
                    html
                );

                /*
                 * Delegated event.
                 * It remains active after tbody HTML is rebuilt.
                 */
                $root.on(
                    "click.VAS044Row",
                    ".VAS-gljr-row",
                    function (event) {
                        event.preventDefault();
                        event.stopPropagation();

                        var journalId =
                            parseInt(
                                $(this).attr(
                                    "data-id"
                                ),
                                10
                            );

                        if (
                            isNaN(journalId) ||
                            journalId <= 0
                        ) {
                            return;
                        }

                        openDetailDialog(
                            journalId
                        );
                    }
                );

                $root.on(
                    "keydown.VAS044Row",
                    ".VAS-gljr-row",
                    function (event) {
                        if (
                            event.key !== "Enter" &&
                            event.key !== " "
                        ) {
                            return;
                        }

                        event.preventDefault();
                        event.stopPropagation();

                        var journalId =
                            parseInt(
                                $(this).attr(
                                    "data-id"
                                ),
                                10
                            );

                        if (
                            isNaN(journalId) ||
                            journalId <= 0
                        ) {
                            return;
                        }

                        openDetailDialog(
                            journalId
                        );
                    }
                );

                $root.on(
                    "click.VAS044Pager",
                    ".VAS-gljr-prev",
                    function () {
                        if (pageNo <= 1) {
                            return;
                        }

                        pageNo--;

                        loadData();
                    }
                );

                $root.on(
                    "click.VAS044Pager",
                    ".VAS-gljr-next",
                    function () {
                        if (
                            totalPages <= 1 ||
                            pageNo >= totalPages
                        ) {
                            return;
                        }

                        pageNo++;

                        loadData();
                    }
                );
            }

            function loadData() {
                if (isDisposed) {
                    return;
                }

                if (
                    loadRequest &&
                    loadRequest.readyState !== 4
                ) {
                    loadRequest.abort();
                }

                showBusy(true);

                loadRequest =
                    $.ajax({
                        url:
                            baseUrl +
                            "VAS/VAS_044_GLJournalRecentWidget/GetRecentEntries",

                        type:
                            "GET",

                        data:
                            {
                                pageNo:
                                    pageNo,

                                pageSize:
                                    pageSize
                            },

                        dataType:
                            "json",

                        cache:
                            false,

                        success:
                            function (result) {
                                if (isDisposed) {
                                    return;
                                }

                                var data =
                                    normalizeResponse(
                                        result
                                    );

                                if (
                                    !data ||
                                    data.success === false ||
                                    data.error
                                ) {
                                    showEmpty(
                                        data &&
                                        (
                                            data.errorText ||
                                            data.error
                                        )
                                    );

                                    return;
                                }

                                currentData =
                                    data;

                                renderRows(
                                    data,
                                    $self
                                        .AD_UserHomeWidgetID
                                );
                            },

                        error:
                            function (
                                xhr,
                                textStatus
                            ) {
                                if (
                                    isDisposed ||
                                    textStatus === "abort"
                                ) {
                                    return;
                                }

                                showEmpty(
                                    getAjaxErrorMessage(
                                        xhr,
                                        lbl(
                                            "VAS_044_LoadFailed",
                                            "Error loading journal entries."
                                        )
                                    )
                                );
                            },

                        complete:
                            function () {
                                loadRequest =
                                    null;

                                if (!isDisposed) {
                                    showBusy(false);
                                }
                            }
                    });
            }

            function renderRows(
                data,
                id
            ) {
                var entries =
                    Array.isArray(
                        data.Entries
                    )
                        ? data.Entries
                        : [];

                var symbol =
                    data.CurSymbol ||
                    data.ISOCode ||
                    "";

                var precision =
                    Number(
                        data.StdPrecision
                    );

                var $tableBody =
                    $root.find(
                        "#VAS-gljr-tbody-" +
                        id
                    );

                if (!entries.length) {
                    $root.find(
                        ".VAS-gljr-table-wrap"
                    ).addClass(
                        "is-empty"
                    );

                    $tableBody.html(
                        '<tr class="VAS-gljr-empty-row">' +

                        '<td colspan="6" class="VAS-gljr-empty">' +

                        esc(
                            lbl(
                                "VIS_NoData",
                                "No data available."
                            )
                        ) +

                        "</td>" +

                        "</tr>"
                    );

                    totalPages =
                        0;

                    totalCount =
                        0;

                    updatePager();

                    return;
                }

                $root.find(
                    ".VAS-gljr-table-wrap"
                ).removeClass(
                    "is-empty"
                );

                totalPages =
                    Number(
                        data.TotalPages ||
                        Math.ceil(
                            Number(
                                data.TotalCount ||
                                entries.length ||
                                0
                            ) /
                            pageSize
                        ) ||
                        1
                    );

                pageSize =
                    Number(
                        data.PageSize ||
                        pageSize ||
                        5
                    );

                pageNo =
                    Number(
                        data.PageNo ||
                        pageNo ||
                        1
                    );

                totalCount =
                    resolveTotalCount(
                        data.TotalCount,
                        entries.length,
                        pageSize,
                        totalPages
                    );

                var html =
                    "";

                for (
                    var index = 0;
                    index < entries.length;
                    index++
                ) {
                    var entry =
                        entries[index] || {};

                    var journalId =
                        parseInt(
                            entry.GL_Journal_ID,
                            10
                        );

                    if (
                        isNaN(journalId) ||
                        journalId <= 0
                    ) {
                        continue;
                    }

                    /*
                     * Value controls the CSS.
                     * Name is displayed to the user.
                     */
                    var statusValue =
                        getStatusValue(
                            entry
                        ).toUpperCase();

                    var statusName =
                        getStatusName(
                            entry
                        );

                    var pillClass =
                        PILL_CLASS[
                        statusValue
                        ] ||
                        "VAS-gljr-pill-draft";

                    /* Currency belongs to the journal's ACCOUNTING SCHEMA, so it
                       differs row by row — format with the row's own symbol /
                       precision and fall back to the response-level values only
                       when the row doesn't carry them. */
                    var rowSymbol =
                        entry.CurSymbol ||
                        entry.ISOCode ||
                        symbol;

                    var rowPrecision =
                        (
                            entry.StdPrecision !== undefined &&
                            entry.StdPrecision !== null &&
                            !isNaN(Number(entry.StdPrecision))
                        )
                            ? Number(entry.StdPrecision)
                            : precision;

                    var debitText =
                        Number(
                            entry.TotalDebit || 0
                        ) !== 0
                            ? formatMoney(
                                rowSymbol,
                                entry.TotalDebit,
                                rowPrecision
                            )
                            : "—";

                    var creditText =
                        Number(
                            entry.TotalCredit || 0
                        ) !== 0
                            ? formatMoney(
                                rowSymbol,
                                entry.TotalCredit,
                                rowPrecision
                            )
                            : "—";

                    var rowClass =
                        "VAS-gljr-row" +
                        (
                            entry.IsUnbalanced
                                ? " VAS-gljr-row-unbal"
                                : ""
                        );

                    var rowInformation =
                        esc(
                            (
                                entry.DocumentNo ||
                                ""
                            ) +

                            " - " +

                            statusName +

                            ", " +

                            lbl(
                                "VAS_044_Debit",
                                "Debit"
                            ) +

                            ": " +

                            debitText +

                            ", " +

                            lbl(
                                "VAS_044_Credit",
                                "Credit"
                            ) +

                            ": " +

                            creditText
                        );

                    html +=
                        '<tr class="' +
                        rowClass +
                        '" ' +

                        'data-id="' +
                        journalId +
                        '" ' +

                        'role="button" ' +

                        'tabindex="0" ' +

                        'title="' +
                        rowInformation +
                        '">' +

                        '<td class="VAS-gljr-col-id">' +
                        esc(
                            entry.DocumentNo
                        ) +
                        "</td>" +

                        '<td class="VAS-gljr-col-date">' +
                        esc(
                            entry.DateAcct
                        ) +
                        "</td>" +

                        '<td class="VAS-gljr-col-desc">' +
                        esc(
                            entry.Description
                        ) +
                        "</td>" +

                        "<td>" +

                        '<span class="VAS-gljr-pill ' +
                        pillClass +
                        '" ' +

                        'data-status-value="' +
                        esc(
                            statusValue
                        ) +
                        '">' +

                        esc(
                            statusName
                        ) +

                        "</span>" +

                        "</td>" +

                        '<td class="VAS-gljr-col-num">' +
                        esc(
                            debitText
                        ) +
                        "</td>" +

                        '<td class="VAS-gljr-col-num">' +
                        esc(
                            creditText
                        ) +
                        "</td>" +

                        "</tr>";
                }

                $tableBody.html(
                    html
                );

                updatePager();

                /* Fit the visible row count to the card height (first paint + resize). */
                observeList();
                if (_needsSync) { scheduleSync(); }
            }

            /* ---- Adaptive row capacity (VAS_020 pattern) ---- */
            function scheduleSync() {
                var raf =
                    window.requestAnimationFrame ||
                    function (cb) { return window.setTimeout(cb, 16); };

                raf(function () { syncCapacity(); });
            }

            function syncCapacity() {
                if (isDisposed) { return; }
                if (loadRequest && loadRequest.readyState !== 4) { return; }   // mid-fetch
                if (totalCount <= 0) { return; }

                var el =
                    $root.find(
                        ".VAS-gljr-table-wrap"
                    )[0];

                if (!el) { return; }

                /* Body rows share the wrap with the sticky header — subtract the
                   thead so capacity counts only the space the rows can occupy. */
                var thead = el.querySelector("thead");
                var theadH = thead ? thead.offsetHeight : 0;
                var avail = el.clientHeight - theadH;

                if (avail <= 0) {
                    if (_needsSync) { scheduleSync(); }   // layout not settled — retry
                    return;
                }

                var rows = el.querySelectorAll("tbody .VAS-gljr-row");
                var maxH = 0;

                for (var i = 0; i < rows.length; i++) {
                    if (rows[i].offsetHeight > maxH) { maxH = rows[i].offsetHeight; }
                }

                if (maxH > 0) { _rowH = maxH; }
                var rowH = _rowH > 0 ? _rowH : LIST_ROW_FALLBACK;

                _needsSync = false;

                var capacity = Math.max(LIST_MIN_ROWS, Math.floor(avail / rowH));

                if (capacity !== pageSize) {
                    pageSize = capacity;

                    /* Clamp the page to the new page count so a grown page size never
                       lands on an empty page. */
                    var maxPages = Math.max(1, Math.ceil(totalCount / pageSize));
                    if (pageNo > maxPages) { pageNo = maxPages; }

                    loadData();
                }
            }

            function observeList() {
                if (typeof ResizeObserver === "undefined") { return; }

                var el =
                    $root.find(
                        ".VAS-gljr-table-wrap"
                    )[0];

                if (!el) { return; }
                if (_listObserver) { _listObserver.disconnect(); }

                _listObserver = new ResizeObserver(function () {
                    if (!(loadRequest && loadRequest.readyState !== 4)) {
                        syncCapacity();
                    }
                });

                _listObserver.observe(el);
            }

            function updatePager() {
                var $pageText =
                    $root.find(
                        ".VAS-gljr-page-text"
                    );

                var $pageCount =
                    $root.find(
                        ".VAS-gljr-page-count"
                    );

                var $previousButton =
                    $root.find(
                        ".VAS-gljr-prev"
                    );

                var $nextButton =
                    $root.find(
                        ".VAS-gljr-next"
                    );

                if (totalCount > 0) {
                    $pageText.text(
                        formatRangeText(
                            pageNo,
                            pageSize,
                            totalCount
                        )
                    );

                    $pageCount.text(
                        pageNo +
                        " " +
                        lbl(
                            "VIS_Of",
                            "of"
                        ) +
                        " " +
                        totalPages
                    );
                }
                else {
                    $pageText.text(
                        ""
                    );

                    $pageCount.text(
                        ""
                    );
                }

                $previousButton.prop(
                    "disabled",
                    pageNo <= 1 ||
                    totalPages <= 1
                );

                $nextButton.prop(
                    "disabled",
                    totalPages <= 1 ||
                    pageNo >= totalPages
                );
            }

            function openDetailDialog(
                journalId
            ) {
                journalId =
                    parseInt(
                        journalId,
                        10
                    );

                if (
                    isNaN(journalId) ||
                    journalId <= 0
                ) {
                    return;
                }

                VAS.GLJournalDetailDialog.open(
                    journalId,
                    {
                        windowNo:
                            $self.windowNo,

                        showDownload:
                            true,

                        onChanged:
                            function () {
                                loadData();
                            }
                    }
                );
            }

            function showEmpty(message) {
                var $tableBody =
                    $root.find(
                        "#VAS-gljr-tbody-" +
                        $self.AD_UserHomeWidgetID
                    );

                currentData =
                    null;

                pageNo =
                    1;

                totalPages =
                    0;

                $root.find(
                    ".VAS-gljr-table-wrap"
                ).addClass(
                    "is-empty"
                );

                $tableBody.html(
                    '<tr class="VAS-gljr-empty-row">' +

                    '<td colspan="6" class="VAS-gljr-empty">' +

                    esc(
                        message ||

                        lbl(
                            "VIS_Error",
                            "Error loading data."
                        )
                    ) +

                    "</td>" +

                    "</tr>"
                );

                totalPages =
                    0;

                totalCount =
                    0;

                updatePager();
            }

            function refreshData() {
                pageNo =
                    1;

                currentData =
                    null;

                loadData();
            }

            this.refreshData =
                refreshData;

            this.getRoot =
                function () {
                    return $root;
                };

            this.disposeComponent =
                function () {
                    isDisposed =
                        true;

                    if (_listObserver) {
                        _listObserver.disconnect();
                        _listObserver = null;
                    }

                    if (
                        loadRequest &&
                        loadRequest.readyState !== 4
                    ) {
                        loadRequest.abort();
                    }

                    $root.off();

                    $root.remove();

                    currentData =
                        null;
                };
        };

    VAS.VAS_044_GLJournalRecentWidget
        .prototype.refreshWidget =
        function () {
            if (
                typeof this.refreshData ===
                "function"
            ) {
                this.refreshData();
            }
        };

    VAS.VAS_044_GLJournalRecentWidget
        .prototype.init =
        function (
            windowNo,
            frame
        ) {
            this.frame =
                frame;

            this.AD_UserHomeWidgetID =
                frame
                    .widgetInfo
                    .AD_UserHomeWidgetID;

            this.windowNo =
                windowNo;

            this.Initalize();

            this.frame
                .getContentGrid()
                .append(
                    this.getRoot()
                );

            ensureDashInlineSizeVar(
                this.getRoot()
            );
        };

    VAS.VAS_044_GLJournalRecentWidget
        .prototype.widgetSizeChange =
        function (
            height,
            width
        ) {
        };

    VAS.VAS_044_GLJournalRecentWidget
        .prototype.dispose =
        function () {
            this.disposeComponent();

            if (this.frame) {
                this.frame.dispose();
            }

            this.frame =
                null;
        };

})(VAS, jQuery);
