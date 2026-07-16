/**
 * GL Journal Pending Action Queue Widget
 * Purpose - Displays GL journals awaiting user action.
 *
 * Status JSON:
 *
 * Status: {
 *     Value: "DR",
 *     Name: "Drafted"
 * }
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                                | Message Key
 * ----+---------------------------------------------+----------------------------
 *  1  | Pending Action Queue                        | VAS_045_PendingActionQueue
 *  2  | Items                                       | VAS_045_Items
 *  3  | Overdue                                     | VAS_045_Overdue
 *  4  | Previous                                    | VIS_Previous
 *  5  | Next                                        | VIS_Next
 *  6  | Close                                       | VAS_Close
 *  7  | Approve                                     | VAS_045_Approve
 *  8  | Post Journal                                | VAS_045_PostJournal
 *  9  | Error Loading Data                          | VIS_Error
 * 10  | No Pending Journals                         | VIS_NoData
 * 11  | Invalid Journal ID                          | VAS_045_InvalidJournalID
 * 12  | Journal Details Not Found                   | VAS_045_JournalDetailsNotFound
 * 13  | Journal Object Was Not Returned             | VAS_045_JournalObjectNotReturned
 * 14  | No Journal Lines Were Returned              | VAS_045_NoJournalLinesReturned
 * 15  | Journal Lines                               | VAS_045_JournalLines
 * 16  | Journal No.                                 | VAS_045_JournalNo
 * 17  | Date                                        | VAS_045_Date
 * 18  | Status                                      | VAS_045_Status
 * 19  | Accounting Book                             | VAS_045_AccountingBook
 * 20  | Total Debit                                 | VAS_045_TotalDebit
 * 21  | Total Credit                                | VAS_045_TotalCredit
 * 22  | Description                                 | VAS_045_Description
 * 23  | Created By                                  | VAS_045_CreatedBy
 * 24  | Drafted                                     | VAS_045_Drafted
 * 25  | Account                                     | VAS_045_Account
 * 26  | Debit                                       | VAS_045_Debit
 * 27  | Credit                                      | VAS_045_Credit
 * 28  | Cost Center                                 | VAS_045_CostCenter
 * 29  | Business Partner                            | VAS_045_BusinessPartner
 * 30  | Product                                     | VAS_045_Product
 * 31  | Project                                     | VAS_045_Project
 * 32  | Total                                       | VAS_045_Total
 * 33  | Approving                                   | VAS_045_Approving
 * 34  | Posting                                     | VAS_045_Posting
 * 35  | Journal Process Failed                      | VAS_045_JournalProcessFailed
 * 36  | Of                                          | VIS_Of
 * 37  | Could Not Load Pending Queue                | VAS_045_LoadPendingQueueFailed
 * ─────────────────────────────────────────────────────────────────────
 */

; VAS = window.VAS || {};

; (function (VAS, $) {
    "use strict";

    VAS.VAS_045_GLJournalPendingWidget = function () {
        this.frame = null;
        this.windowNo = 0;
        this.AD_UserHomeWidgetID = 0;

        var $self = this;

        var $root = $(
            '<div class="VAS-gljpq-root">'
        );

        var $detailDialog = null;
        var $detailBody = null;
        var $detailBusy = null;

        var $postButton = null;
        var $detailCloseButton = null;

        var currentData = null;

        var pageNo = 1;
        var pageSize = 2;                 // initial guess; replaced by the adaptive measure
        var totalPages = 0;
        var totalCount = 0;

        /* Adaptive page size (VAS_020 pattern): derive the visible row count from the
           list's available height at runtime so the rows always fit the card (no clipped
           last row); a ResizeObserver re-pages on resize. Measure only on the first paint
           (via _needsSync) and on resize (via the observer) — never on manual navigation. */
        var _rowH = 0;                    // last measured row height (px)
        var _needsSync = true;            // measure capacity on first paint only
        var _listObserver = null;
        var LIST_MIN_ROWS = 1;            // never force more rows than physically fit
        var LIST_ROW_FALLBACK = 44;       // px, used only before a real row is measured

        var selectedJournalId = 0;
        var selectedJournalStatus = "";
        var selectedJournalPosted = false;

        var detailLoaded = false;
        var detailLinePageNo = 1;
        var detailLinePageSize = 3;
        var detailLineTotalPages = 1;
        var journalActionInProgress = false;
        var isDisposed = false;

        var refreshTimer = null;
        var loadRequest = null;
        var detailRequest = null;
        var actionRequest = null;

        var baseUrl =
            VIS.Application.contextUrl;

        var PILL_CLASS = {
            "DR": "VAS-gljpq-pill-draft",
            "CO": "VAS-gljpq-pill-posted",
            "CL": "VAS-gljpq-pill-posted",
            "IP": "VAS-gljpq-pill-submit",
            "AP": "VAS-gljpq-pill-posted",
            "NA": "VAS-gljpq-pill-pending",
            "VO": "VAS-gljpq-pill-voided",
            "RE": "VAS-gljpq-pill-returned"
        };

        this.Initalize = function () {
            createWidget();
            createBusyIndicator();

            showBusy(true);
            loadData();

            refreshTimer =
                window.setInterval(
                    function () {
                        if (
                            !isDisposed &&
                            !journalActionInProgress
                        ) {
                            refreshData();
                        }
                    },
                    1000 * 60 * 5
                );
        };

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

        function fmtAmt(
            amount,
            precision
        ) {
            var standardPrecision = 2;

            try {
                if (
                    VIS &&
                    VIS.Env &&
                    VIS.Env.getCtx &&
                    VIS.Env.getCtx().getStdPrecision
                ) {
                    standardPrecision =
                        Number(
                            VIS.Env
                                .getCtx()
                                .getStdPrecision()
                        );
                }
            }
            catch (error) {
                standardPrecision = 2;
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
                resolvedPrecision = 2;
            }

            var numericAmount =
                Number(amount || 0);

            if (isNaN(numericAmount)) {
                numericAmount = 0;
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

        function normalizeResponse(result) {
            var data = result;

            if (
                data &&
                data.d !== undefined
            ) {
                data = data.d;
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
                    data = data.d;
                }
            }

            return data;
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
                            "Error Loading Data."
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
                        "Error Loading Data."
                    )
                );
            }

            if (
                xhr &&
                xhr.status
            ) {
                return (
                    fallback ||
                    lbl(
                        "VIS_Error",
                        "Error Loading Data."
                    )
                ) +
                    " HTTP " +
                    xhr.status;
            }

            return (
                fallback ||
                lbl(
                    "VIS_Error",
                    "Error Loading Data."
                )
            );
        }

        function getResponseMessage(
            response,
            fallback
        ) {
            if (!response) {
                return fallback;
            }

            if (response.errorKey || response.messageKey) {
                return lbl(
                    response.errorKey || response.messageKey,
                    response.errorText ||
                    response.error ||
                    response.message ||
                    fallback
                );
            }

            return (
                response.errorText ||
                response.error ||
                response.message ||
                fallback
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

        /**
         * Returns the original stored reference value.
         *
         * Supports:
         * Status.Value
         * StatusValue
         * DocStatus
         */
        function getStatusValue(source) {
            source = source || {};

            if (
                source.Status &&
                source.Status.Value != null
            ) {
                return String(
                    source.Status.Value
                );
            }

            if (
                source.StatusValue != null
            ) {
                return String(
                    source.StatusValue
                );
            }

            return String(
                source.DocStatus || ""
            );
        }

        /**
         * Returns the translated display name.
         *
         * Supports:
         * Status.Name
         * StatusName
         * Status.Value
         * DocStatus
         */
        function getStatusName(source) {
            source = source || {};

            if (
                source.Status &&
                source.Status.Name
            ) {
                return String(
                    source.Status.Name
                );
            }

            if (source.StatusName) {
                return String(
                    source.StatusName
                );
            }

            return getStatusValue(source);
        }

        function getJournalId(item) {
            if (!item) {
                return 0;
            }

            var id =
                item.GL_Journal_ID;

            if (
                id === undefined ||
                id === null ||
                id === ""
            ) {
                id = item.JournalID;
            }

            if (
                id === undefined ||
                id === null ||
                id === ""
            ) {
                id = item.journalId;
            }

            if (
                id === undefined ||
                id === null ||
                id === ""
            ) {
                id = item.GLJournalID;
            }

            if (
                id === undefined ||
                id === null ||
                id === ""
            ) {
                id = item.GL_JournalId;
            }

            id =
                parseInt(
                    id,
                    10
                );

            if (isNaN(id)) {
                return 0;
            }

            return id;
        }

        function createBusyIndicator() {
            var $busy =
                $(
                    '<div id="VAS-gljpq-busy-' +
                    $self.AD_UserHomeWidgetID +
                    '" class="vis-busyindicatorouterwrap">' +

                    '<div class="vis-busyindicatorinnerwrap">' +

                    '<i class="vis_widgetloader"></i>' +

                    "</div>" +

                    "</div>"
                );

            $root.append($busy);
        }

        function showBusy(show) {
            if (
                !$root ||
                !$root.length
            ) {
                return;
            }

            var $busy =
                $root.find(
                    "#VAS-gljpq-busy-" +
                    $self.AD_UserHomeWidgetID
                );

            if (show) {
                $busy.show();
            }
            else {
                $busy.hide();
            }
        }

        function showDetailBusy(show) {
            if (
                !$detailBusy ||
                !$detailBusy.length
            ) {
                return;
            }

            if (show) {
                $detailBusy.show();
            }
            else {
                $detailBusy.hide();
            }
        }

        function clockIconSvg() {
            return (
                '<svg viewBox="0 0 24 24" ' +
                'fill="none" ' +
                'stroke="currentColor" ' +
                'stroke-width="2" ' +
                'stroke-linecap="round" ' +
                'stroke-linejoin="round">' +

                '<circle cx="12" cy="12" r="10"></circle>' +

                '<polyline points="12 6 12 12 16 14"></polyline>' +

                "</svg>"
            );
        }

        function createWidget() {
            var id =
                $self.AD_UserHomeWidgetID;

            var clockIcon =
                clockIconSvg();

            var html =
                '<div class="VAS-gljpq-card">' +

                '<div class="w-head">' +

                '<div class="VAS-gljpq-icon">' +
                clockIcon +
                "</div>" +

                '<div class="w-title">' +

                esc(
                    lbl(
                        "VAS_045_PendingActionQueue",
                        "Pending Action Queue"
                    )
                ) +

                "</div>" +

                '<span class="VAS-gljpq-count" ' +

                'id="VAS-gljpq-count-' +
                id +
                '"></span>' +

                "</div>" +

                '<div class="VAS-gljpq-body" ' +

                'id="VAS-gljpq-body-' +
                id +
                '"></div>' +

                '<div class="VAS-gljpq-pager">' +

                '<span class="VAS-gljpq-page-text"></span>' +

                '<button type="button" ' +

                'class="VAS-gljpq-page-btn VAS-gljpq-prev" ' +

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

                '<span class="VAS-gljpq-page-count"></span>' +

                '<button type="button" ' +

                'class="VAS-gljpq-page-btn VAS-gljpq-next" ' +

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

            $root.append(html);

            $root.on(
                "click.VAS045Pager",
                ".VAS-gljpq-prev",
                function () {
                    if (pageNo <= 1) {
                        return;
                    }

                        pageNo--;

                    loadData();
                }
            );

            $root.on(
                "click.VAS045Pager",
                ".VAS-gljpq-next",
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

            /*
             * Delegated click remains active after
             * rebuilding the queue HTML.
             */
            $root.on(
                "click.VAS045Journal",
                ".VAS-gljpq-item",
                function (event) {
                    event.preventDefault();
                    event.stopPropagation();

                    var journalId =
                        parseInt(
                            $(this).attr(
                                "data-journal-id"
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
                "keydown.VAS045Journal",
                ".VAS-gljpq-item",
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
                                "data-journal-id"
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

            createDetailDialog(
                clockIcon
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
                        "VAS/VAS_045_GLJournalPendingWidget/GetPendingQueue",

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

                            if (!data) {
                                showError(
                                    lbl(
                                        "VIS_Error",
                                        "Error Loading Data."
                                    )
                                );

                                return;
                            }

                            if (
                                data.success === false ||
                                data.error
                            ) {
                                showError(
                                    getResponseMessage(
                                        data,
                                        lbl(
                                            "VAS_045_LoadPendingQueueFailed",
                                            "Could Not Load Pending Queue"
                                        )
                                    )
                                );

                                return;
                            }

                            if (
                                !Array.isArray(
                                    data.Queue
                                )
                            ) {
                                showError(
                                    lbl(
                                        "VIS_Error",
                                        "Error Loading Data."
                                    )
                                );

                                return;
                            }

                            currentData =
                                data;

                            render(data);
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

                            showError(
                                getAjaxErrorMessage(
                                    xhr,
                                    lbl(
                                        "VAS_045_LoadPendingQueueFailed",
                                        "Error Loading Pending Journal Queue."
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

        function render(data) {
            var id =
                $self.AD_UserHomeWidgetID;

            var $body =
                $root.find(
                    "#VAS-gljpq-body-" +
                    id
                );

            var queue =
                Array.isArray(
                    data.Queue
                )
                    ? data.Queue
                    : [];

            var precision =
                Number(
                    data.StdPrecision
                );

            var symbol =
                data.CurSymbol ||
                data.currencySymbol ||
                data.ISOCode ||
                data.currencyISO ||
                "";

            $root.find(
                "#VAS-gljpq-count-" +
                id
            ).text(
                Number(
                    data.TotalCount ||
                    queue.length ||
                    0
                ).toLocaleString(
                    window.navigator.language
                ) +

                " " +

                lbl(
                    "VAS_045_Items",
                    "Items"
                )
            );

            if (!queue.length) {
                $body
                    .addClass(
                        "is-empty"
                    )
                    .html(
                        '<div class="VAS-gljpq-empty">' +

                        esc(
                            lbl(
                                "VIS_NoData",
                                "No Pending Journals."
                            )
                        ) +

                        "</div>"
                    );

                totalPages =
                    0;

                totalCount =
                    0;

                updatePager();

                return;
            }

            $body.removeClass(
                "is-empty"
            );

            totalPages =
                Number(
                    data.TotalPages ||
                    Math.ceil(
                        Number(
                            data.TotalCount ||
                            queue.length ||
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
                    3
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
                    queue.length,
                    pageSize,
                    totalPages
                );

            var html =
                '<div class="VAS-gljpq-list">';

            for (
                var index = 0;
                index < queue.length;
                index++
            ) {
                var item =
                    queue[index] || {};

                var journalId =
                    getJournalId(item);

                if (journalId <= 0) {
                    continue;
                }

                /*
                 * Status.Value controls CSS and actions.
                 * Status.Name is displayed to the user.
                 */
                var statusValue =
                    getStatusValue(
                        item
                    ).toUpperCase();

                var statusName =
                    getStatusName(
                        item
                    );

                var titleText =
                    item.DocumentNo || "";

                if (item.Description) {
                    titleText +=
                        " \u00B7 " +
                        item.Description;
                }

                var ageText =
                    item.AgeStr || "";

                if (item.IsOverdue) {
                    ageText +=
                        " " +

                        lbl(
                            "VAS_045_Overdue",
                            "Overdue"
                        );
                }

                var metaParts = [];

                if (statusName) {
                    metaParts.push(
                        esc(statusName)
                    );
                }

                if (item.ActionLabel) {
                    metaParts.push(
                        esc(
                            item.ActionLabel
                        )
                    );
                }

                if (ageText) {
                    metaParts.push(
                        esc(ageText)
                    );
                }

                if (item.UserName) {
                    metaParts.push(
                        esc(
                            item.UserName
                        )
                    );
                }

                var amountText =
                    symbol +
                    fmtAmt(
                        item.TotalDebit,
                        precision
                    );

                var markerType =
                    String(
                        item.MarkerType ||
                        "normal"
                    ).toLowerCase();

                var pillClass =
                    PILL_CLASS[
                    statusValue
                    ] ||
                    "VAS-gljpq-pill-draft";

                var itemInformation =
                    (
                        item.DocumentNo ||
                        ""
                    ) +

                    " - " +

                    statusName +

                    ", " +

                    (
                        item.ActionLabel ||
                        ""
                    ) +

                    ", " +

                    ageText +

                    ", " +

                    amountText;

                html +=
                    '<div class="VAS-gljpq-item" ' +

                    'data-journal-id="' +
                    journalId +
                    '" ' +

                    'data-status-value="' +
                    esc(statusValue) +
                    '" ' +

                    'role="button" ' +

                    'tabindex="0" ' +

                    'title="' +
                    esc(itemInformation) +
                    '">' +

                    '<div class="VAS-gljpq-mrk ' +

                    'VAS-gljpq-mrk-' +
                    esc(markerType) +
                    '"></div>' +

                    '<div class="VAS-gljpq-body-row">' +

                    '<div class="VAS-gljpq-title">' +

                    esc(titleText) +

                    "</div>" +

                    '<div class="VAS-gljpq-meta">' +

                    '<span class="VAS-gljpq-pill ' +
                    pillClass +
                    '">' +

                    esc(statusName) +

                    "</span>" +

                    (
                        metaParts.length
                            ? (
                                " \u00B7 " +
                                metaParts
                                    .slice(1)
                                    .join(" \u00B7 ")
                            )
                            : ""
                    ) +

                    "</div>" +

                    "</div>" +

                    '<span class="VAS-gljpq-amt">' +

                    esc(amountText) +

                    "</span>" +

                    "</div>";
            }

            html +=
                "</div>";

            $body.html(html);

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
                    "#VAS-gljpq-body-" +
                    $self.AD_UserHomeWidgetID
                )[0];

            if (!el) { return; }

            var avail = el.clientHeight;

            if (avail <= 0) {
                if (_needsSync) { scheduleSync(); }   // layout not settled — retry
                return;
            }

            /* Size off the tallest rendered row so a wrapped title never clips. */
            var rows = el.querySelectorAll(".VAS-gljpq-item");
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

                /* Clamp the current page to the new page count so a grown page size
                   never lands on an empty page. */
                var maxPages = Math.max(1, Math.ceil(totalCount / pageSize));
                if (pageNo > maxPages) { pageNo = maxPages; }

                loadData();
            }
        }

        function observeList() {
            if (typeof ResizeObserver === "undefined") { return; }

            var el =
                $root.find(
                    "#VAS-gljpq-body-" +
                    $self.AD_UserHomeWidgetID
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
                    ".VAS-gljpq-page-text"
                );

            var $pageCount =
                $root.find(
                    ".VAS-gljpq-page-count"
                );

            var $previousButton =
                $root.find(
                    ".VAS-gljpq-prev"
                );

            var $nextButton =
                $root.find(
                    ".VAS-gljpq-next"
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
                $pageText.text("");
                $pageCount.text("");
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

        function createDetailDialog(
            svgIcon
        ) {
            var id =
                $self.AD_UserHomeWidgetID;

            $detailDialog =
                $(
                    '<div class="VAS-gljpq-dialog" ' +

                    'id="VAS-gljpq-dialog-' +
                    id +
                    '" ' +

                    'role="dialog" ' +

                    'aria-modal="true">' +

                    '<div class="VAS-gljpq-dialog-scrim"></div>' +

                    '<div class="VAS-gljpq-dialog-card">' +

                    '<div class="VAS-gljpq-dialog-head">' +

                    '<div class="VAS-gljpq-dialog-icon">' +
                    svgIcon +
                    "</div>" +

                    '<div class="VAS-gljpq-dialog-title-wrap">' +

                    '<div class="VAS-gljpq-dialog-title" ' +

                    'id="VAS-gljpq-dialog-title-' +
                    id +
                    '">' +

                    "&mdash;" +

                    "</div>" +

                    '<div class="VAS-gljpq-dialog-sub" ' +

                    'id="VAS-gljpq-dialog-sub-' +
                    id +
                    '">' +

                    "&mdash;" +

                    "</div>" +

                    "</div>" +

                    '<button type="button" ' +

                    'class="VAS-gljpq-dialog-close-x" ' +

                    'aria-label="' +

                    esc(
                        lbl(
                            "VAS_Close",
                            "Close"
                        )
                    ) +

                    '">' +

                    '<svg viewBox="0 0 24 24" ' +

                    'fill="none" ' +

                    'stroke="currentColor" ' +

                    'stroke-width="1.8" ' +

                    'stroke-linecap="round" ' +

                    'stroke-linejoin="round">' +

                    '<line x1="18" y1="6" x2="6" y2="18"></line>' +

                    '<line x1="6" y1="6" x2="18" y2="18"></line>' +

                    "</svg>" +

                    "</button>" +

                    "</div>" +

                    '<div class="VAS-gljpq-dialog-body">' +

                    '<div class="VAS-gljpq-dialog-busy">' +

                    '<div class="vis-busyindicatorinnerwrap">' +

                    '<i class="vis_widgetloader"></i>' +

                    "</div>" +

                    "</div>" +

                    '<div class="VAS-gljpq-detail-content" ' +

                    'id="VAS-gljpq-detail-body-' +
                    id +
                    '"></div>' +

                    "</div>" +

                    '<div class="VAS-gljpq-dialog-footer">' +

                    '<div class="VAS-gljpq-dialog-actions">' +

                    '<button type="button" ' +

                    'class="VAS-gljpq-dialog-primary VAS-gljpq-action-post">' +

                    esc(
                        lbl(
                            "VAS_045_PostJournal",
                            "Post Journal"
                        )
                    ) +

                    "</button>" +

                    '<button type="button" ' +

                    'class="VAS-gljpq-dialog-primary VAS-gljpq-detail-close">' +

                    esc(
                        lbl(
                            "VAS_Close",
                            "Close"
                        )
                    ) +

                    "</button>" +

                    "</div>" +

                    "</div>" +

                    "</div>" +

                    "</div>"
                );

            $detailBody =
                $detailDialog.find(
                    "#VAS-gljpq-detail-body-" +
                    id
                );

            $detailBusy =
                $detailDialog.find(
                    ".VAS-gljpq-dialog-busy"
                );

            $postButton =
                $detailDialog.find(
                    ".VAS-gljpq-action-post"
                );

            $detailCloseButton =
                $detailDialog.find(
                    ".VAS-gljpq-detail-close"
                );

            showDetailBusy(false);

            updateActionButtons();

            $postButton.on(
                "click.VAS045Post",
                function () {
                    executeJournalAction(
                        "PostJournal",
                        "post"
                    );
                }
            );

            $detailDialog.find(
                ".VAS-gljpq-dialog-close-x, " +
                ".VAS-gljpq-detail-close, " +
                ".VAS-gljpq-dialog-scrim"
            ).on(
                "click.VAS045Close",
                closeDetailDialog
            );

            $(document).on(
                "keydown.VAS-gljpq-" +
                id,
                function (event) {
                    if (
                        event.key === "Escape" &&
                        $detailDialog &&
                        $detailDialog.is(
                            ":visible"
                        ) &&
                        !journalActionInProgress
                    ) {
                        closeDetailDialog();
                    }
                }
            );

            $("body").append(
                $detailDialog
            );

            $detailDialog.hide();
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
                !$detailDialog ||
                !$detailDialog.length
            ) {
                return;
            }

            selectedJournalId =
                journalId;

            selectedJournalStatus =
                "";

            selectedJournalPosted =
                false;

            detailLoaded =
                false;

            detailLinePageNo =
                1;

            detailLineTotalPages =
                1;

            resetDetailDialog();
            updateActionButtons();

            $detailDialog.show();

            $("body").addClass(
                "VAS-gljpq-body-lock"
            );

            if (
                isNaN(journalId) ||
                journalId <= 0
            ) {
                renderDetailError(
                    lbl(
                        "VAS_045_InvalidJournalID",
                        "Invalid Journal ID."
                    )
                );

                return;
            }

            loadJournalDetail(
                journalId
            );
        }

        function resetDetailDialog() {
            if (!$detailDialog) {
                return;
            }

            var id =
                $self.AD_UserHomeWidgetID;

            $detailDialog.find(
                "#VAS-gljpq-dialog-title-" +
                id
            ).html(
                "&mdash;"
            );

            $detailDialog.find(
                "#VAS-gljpq-dialog-sub-" +
                id
            ).html(
                "&mdash;"
            );

            if ($detailBody) {
                $detailBody.empty();

                $detailBody
                    .off(".VAS045LinePager")
                    .on(
                        "click.VAS045LinePager",
                        ".VAS-gljpq-line-prev, .VAS-gljpq-line-next",
                        function () {
                            if (
                                journalActionInProgress ||
                                selectedJournalId <= 0
                            ) {
                                return;
                            }

                            var nextPage =
                                $(this).hasClass(
                                    "VAS-gljpq-line-prev"
                                )
                                    ? detailLinePageNo - 1
                                    : detailLinePageNo + 1;

                            if (
                                nextPage < 1 ||
                                nextPage > detailLineTotalPages
                            ) {
                                return;
                            }

                            detailLinePageNo =
                                nextPage;

                            loadJournalDetail(
                                selectedJournalId
                            );
                        }
                    );
            }
        }

        function closeDetailDialog() {
            if (
                !$detailDialog ||
                journalActionInProgress
            ) {
                return;
            }

            if (
                detailRequest &&
                detailRequest.readyState !== 4
            ) {
                detailRequest.abort();
            }

            selectedJournalId =
                0;

            selectedJournalStatus =
                "";

            selectedJournalPosted =
                false;

            detailLoaded =
                false;

            updateActionButtons();

            $detailDialog.hide();

            $("body").removeClass(
                "VAS-gljpq-body-lock"
            );
        }

        function loadJournalDetail(
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
                renderDetailError(
                    lbl(
                        "VAS_045_InvalidJournalID",
                        "Invalid Journal ID."
                    )
                );

                return;
            }

            if (
                detailRequest &&
                detailRequest.readyState !== 4
            ) {
                detailRequest.abort();
            }

            detailLoaded =
                false;

            updateActionButtons();
            showDetailBusy(true);

            if ($detailBody) {
                $detailBody.empty();
            }

            /*
             * Details are loaded from VAS_041 because that controller
             * owns GetJournalEntryDetail.
             */
            detailRequest =
                $.ajax({
                    url:
                        baseUrl +
                        "VAS/VAS_041_GLJournalEntriesWidget/GetJournalEntryDetail",

                    type:
                        "GET",

                    dataType:
                        "json",

                    cache:
                        false,

                    data: {
                        journalId:
                            journalId,

                        pageNo:
                            detailLinePageNo,

                        pageSize:
                            detailLinePageSize
                    },

                    success:
                        function (result) {
                            if (isDisposed) {
                                return;
                            }

                            var data =
                                normalizeResponse(
                                    result
                                );

                            if (!data) {
                                renderDetailError(
                                    lbl(
                                        "VAS_045_JournalDetailsNotFound",
                                        "Journal Details Not Found."
                                    )
                                );

                                return;
                            }

                            if (
                                data.success === false ||
                                data.error
                            ) {
                                renderDetailError(
                                    getResponseMessage(
                                        data,
                                        lbl(
                                            "VAS_045_JournalDetailsNotFound",
                                            "Journal Details Not Found."
                                        )
                                    )
                                );

                                return;
                            }

                            if (
                                !data.Journal &&
                                data.journal
                            ) {
                                data.Journal =
                                    data.journal;
                            }

                            if (
                                !data.Lines &&
                                data.lines
                            ) {
                                data.Lines =
                                    data.lines;
                            }

                            if (
                                !data.Lines &&
                                data.JournalLines
                            ) {
                                data.Lines =
                                    data.JournalLines;
                            }

                            if (
                                !data.Lines &&
                                data.journalLines
                            ) {
                                data.Lines =
                                    data.journalLines;
                            }

                            if (!data.Journal) {
                                renderDetailError(
                                    lbl(
                                        "VAS_045_JournalObjectNotReturned",
                                        "Journal Object Was Not Returned From Server."
                                    )
                                );

                                return;
                            }

                            renderJournalDetail(
                                data
                            );
                        },

                    error:
                        function (
                            xhr,
                            textStatus,
                            errorThrown
                        ) {
                            if (
                                isDisposed ||
                                textStatus === "abort"
                            ) {
                                return;
                            }

                            var message =
                                getAjaxErrorMessage(
                                    xhr,
                                    errorThrown ||
                                    lbl(
                                        "VAS_045_JournalDetailsNotFound",
                                        "Journal Details Not Found."
                                    )
                                );

                            if (
                                window.console &&
                                console.error
                            ) {
                                console.error(
                                    "GetJournalEntryDetail failed:",
                                    {
                                        journalId:
                                            journalId,

                                        status:
                                            xhr
                                                ? xhr.status
                                                : 0,

                                        responseText:
                                            xhr
                                                ? xhr.responseText
                                                : "",

                                        error:
                                            errorThrown
                                    }
                                );
                            }

                            renderDetailError(
                                message
                            );
                        },

                    complete:
                        function () {
                            detailRequest =
                                null;

                            if (!isDisposed) {
                                showDetailBusy(false);
                            }
                        }
                });
        }

        function renderJournalDetail(
            data
        ) {
            var journal =
                data.Journal ||
                data.journal ||
                {};

            var lines =
                data.Lines ||
                data.lines ||
                data.JournalLines ||
                data.journalLines ||
                [];

            if (!Array.isArray(lines)) {
                lines = [];
            }

            detailLinePageNo =
                Number(
                    data.LinePageNo ||
                    data.linePageNo ||
                    detailLinePageNo ||
                    1
                );

            detailLinePageSize =
                Number(
                    data.LinePageSize ||
                    data.linePageSize ||
                    detailLinePageSize ||
                    3
                );

            detailLineTotalPages =
                Math.max(
                    Number(
                        data.LineTotalPages ||
                        data.lineTotalPages ||
                        1
                    ),
                    1
                );

            var detailLineCount =
                Number(
                    data.LineCount ||
                    data.lineCount ||
                    lines.length ||
                    0
                );

            var precision =
                data.StdPrecision;

            if (
                precision === undefined ||
                precision === null
            ) {
                precision =
                    data.stdPrecision;
            }

            var returnedJournalId =
                parseInt(
                    journal.GL_Journal_ID,
                    10
                );

            if (
                !isNaN(returnedJournalId) &&
                returnedJournalId > 0
            ) {
                selectedJournalId =
                    returnedJournalId;
            }

            /*
             * Status.Value is used for CSS and actions.
             * Status.Name is displayed.
             */
            selectedJournalStatus =
                getStatusValue(
                    journal
                ).toUpperCase();

            selectedJournalPosted =
                isPosted(
                    journal.Posted
                );

            var statusName =
                getStatusName(
                    journal
                );

            var pillClass =
                PILL_CLASS[
                selectedJournalStatus
                ] ||
                "VAS-gljpq-pill-draft";

            var id =
                $self.AD_UserHomeWidgetID;

            var totalDebit =
                fmtAmt(
                    journal.TotalDebit,
                    precision
                );

            var totalCredit =
                fmtAmt(
                    journal.TotalCredit,
                    precision
                );

            var accountingBook =
                journal.AccountingBook ||
                "Primary";

            var currencyText =
                data.ISOCode ||
                data.currencyISO ||
                data.CurSymbol ||
                data.currencySymbol ||
                "";

            $detailDialog.find(
                "#VAS-gljpq-dialog-title-" +
                id
            ).text(
                (
                    journal.DocumentNo ||
                    ""
                ) +

                (
                    journal.Description
                        ? (
                            " \u00B7 " +
                            journal.Description
                        )
                        : ""
                )
            );

            $detailDialog.find(
                "#VAS-gljpq-dialog-sub-" +
                id
            ).text(
                statusName +

                (
                    journal.DateAcct
                        ? (
                            " \u00B7 " +
                            journal.DateAcct
                        )
                        : ""
                )
            );

            var html =
                '<div class="VAS-gljpq-detail-summary">' +

                "<div>" +

                "<span>" +

                esc(
                    lbl(
                        "VAS_045_JournalNo",
                        "Journal No."
                    )
                ) +

                "</span>" +

                "<strong>" +

                esc(
                    journal.DocumentNo
                ) +

                "</strong>" +

                "</div>" +

                "<div>" +

                "<span>" +

                esc(
                    lbl(
                        "VAS_045_Date",
                        "Date"
                    )
                ) +

                "</span>" +

                "<strong>" +

                esc(
                    journal.DateAcct
                ) +

                "</strong>" +

                "</div>" +

                "<div>" +

                "<span>" +

                esc(
                    lbl(
                        "VAS_045_Status",
                        "Status"
                    )
                ) +

                "</span>" +

                "<strong>" +

                '<span class="VAS-gljpq-pill ' +
                pillClass +
                '" ' +

                'data-status-value="' +
                esc(
                    selectedJournalStatus
                ) +
                '">' +

                "<span></span>" +

                esc(statusName) +

                "</span>" +

                "</strong>" +

                "</div>" +

                "<div>" +

                "<span>" +

                esc(
                    lbl(
                        "VAS_045_AccountingBook",
                        "Accounting Book"
                    )
                ) +

                "</span>" +

                "<strong>" +

                esc(accountingBook) +

                "</strong>" +

                "</div>" +

                "<div>" +

                "<span>" +

                esc(
                    lbl(
                        "VAS_045_Currency",
                        "Currency"
                    )
                ) +

                "</span>" +

                "<strong>" +

                esc(currencyText) +

                "</strong>" +

                "</div>" +

                "<div>" +

                "<span>" +

                esc(
                    lbl(
                        "VAS_045_TotalDebit",
                        "Total Debit"
                    )
                ) +

                "</span>" +

                "<strong>" +

                esc(totalDebit) +

                "</strong>" +

                "</div>" +

                "<div>" +

                "<span>" +

                esc(
                    lbl(
                        "VAS_045_TotalCredit",
                        "Total Credit"
                    )
                ) +

                "</span>" +

                "<strong>" +

                esc(totalCredit) +

                "</strong>" +

                "</div>" +

                "<div>" +

                "<span>" +

                esc(
                    lbl(
                        "VAS_045_Description",
                        "Description"
                    )
                ) +

                "</span>" +

                "<strong>" +

                esc(
                    journal.Description
                ) +

                "</strong>" +

                "</div>" +

                "</div>" +

                '<div class="VAS-gljpq-detail-lines-wrap">' +

                '<table class="VAS-gljpq-detail-lines">' +

                "<thead>" +

                "<tr>" +

                "<th>" +
                esc(
                    lbl(
                        "VAS_045_Account",
                        "Account"
                    )
                ) +
                "</th>" +

                "<th>" +
                esc(
                    lbl(
                        "VAS_045_Debit",
                        "Debit"
                    )
                ) +
                "</th>" +

                "<th>" +
                esc(
                    lbl(
                        "VAS_045_Credit",
                        "Credit"
                    )
                ) +
                "</th>" +

                "<th>" +
                esc(
                    lbl(
                        "VAS_045_CostCenter",
                        "Cost Center"
                    )
                ) +
                "</th>" +

                "<th>" +
                esc(
                    lbl(
                        "VAS_045_BusinessPartner",
                        "Business Partner"
                    )
                ) +
                "</th>" +

                "<th>" +
                esc(
                    lbl(
                        "VAS_045_Product",
                        "Product"
                    )
                ) +
                "</th>" +

                "<th>" +
                esc(
                    lbl(
                        "VAS_045_Project",
                        "Project"
                    )
                ) +
                "</th>" +

                "</tr>" +

                "</thead>" +

                "<tbody>";

            if (!lines.length) {
                html +=
                    "<tr>" +

                    '<td colspan="7" ' +

                    'class="VAS-gljpq-detail-empty-line">' +

                    esc(
                        lbl(
                            "VAS_045_NoJournalLinesReturned",
                            "No Journal Lines Were Returned From Server."
                        )
                    ) +

                    "</td>" +

                    "</tr>";
            }
            else {
                for (
                    var lineIndex = 0;
                    lineIndex < lines.length;
                    lineIndex++
                ) {
                    var line =
                        lines[lineIndex] || {};

                    var accountText;

                    if (
                        line.AccountCode &&
                        line.AccountName
                    ) {
                        accountText =
                            line.AccountCode +
                            " \u00B7 " +
                            line.AccountName;
                    }
                    else {
                        accountText =
                            line.AccountCode ||
                            line.AccountName ||
                            "-";
                    }

                    html +=
                        "<tr>" +

                        "<td>" +

                        esc(accountText) +

                        "</td>" +

                        '<td class="VAS-gljpq-detail-amt">' +

                        esc(
                            Number(
                                line.Debit || 0
                            ) > 0
                                ? fmtAmt(
                                    line.Debit,
                                    precision
                                )
                                : "-"
                        ) +

                        "</td>" +

                        '<td class="VAS-gljpq-detail-amt">' +

                        esc(
                            Number(
                                line.Credit || 0
                            ) > 0
                                ? fmtAmt(
                                    line.Credit,
                                    precision
                                )
                                : "-"
                        ) +

                        "</td>" +

                        "<td>" +

                        esc(
                            line.CostCenter ||
                            "-"
                        ) +

                        "</td>" +

                        "<td>" +

                        esc(
                            line.BPartner ||
                            "-"
                        ) +

                        "</td>" +

                        "<td>" +

                        esc(
                            line.Product ||
                            "-"
                        ) +

                        "</td>" +

                        "<td>" +

                        esc(
                            line.Project ||
                            "-"
                        ) +

                        "</td>" +

                        "</tr>";
                }
            }

            html +=
                "</tbody>" +

                "<tfoot>" +

                "<tr>" +

                "<td>" +

                esc(
                    lbl(
                        "VAS_045_Total",
                        "Total"
                    )
                ) +

                "</td>" +

                '<td class="VAS-gljpq-detail-amt">' +

                esc(totalDebit) +

                "</td>" +

                '<td class="VAS-gljpq-detail-amt">' +

                esc(totalCredit) +

                "</td>" +

                '<td colspan="4"></td>' +

                "</tr>" +

                "</tfoot>" +

                "</table>" +

                "</div>" +

                '<div class="VAS-gljpq-line-pager">' +

                '<span class="VAS-gljpq-page-text">' +
                esc(
                    detailLineCount
                        ? formatRangeText(
                            detailLinePageNo,
                            detailLinePageSize,
                            detailLineCount
                        )
                        : ""
                ) +
                "</span>" +

                '<button type="button" ' +
                'class="VAS-gljpq-page-btn VAS-gljpq-line-prev" ' +
                (
                    detailLinePageNo <= 1 ||
                    detailLineTotalPages <= 1
                        ? "disabled "
                        : ""
                ) +
                'aria-label="Previous">&#8249;</button>' +

                '<span class="VAS-gljpq-page-count">' +
                esc(
                    detailLineCount
                        ? (
                            detailLinePageNo +
                            " of " +
                            detailLineTotalPages
                        )
                        : ""
                ) +
                "</span>" +

                '<button type="button" ' +
                'class="VAS-gljpq-page-btn VAS-gljpq-line-next" ' +
                (
                    detailLinePageNo >= detailLineTotalPages ||
                    detailLineTotalPages <= 1
                        ? "disabled "
                        : ""
                ) +
                'aria-label="Next">&#8250;</button>' +

                "</div>" +

                '<div class="VAS-gljpq-created-strip">' +

                '<span class="VAS-gljpq-avatar">' +

                esc(
                    initials(
                        journal.CreatedByName
                    )
                ) +

                "</span>" +

                "<div>" +

                "<span>" +

                esc(
                    lbl(
                        "VAS_045_CreatedBy",
                        "Created By"
                    )
                ) +

                "</span>" +

                "<strong>" +

                esc(
                    journal.CreatedByName ||
                    "-"
                ) +

                "</strong>" +

                (
                    journal.CreatedDate
                        ? (
                            " \u00B7 " +

                            esc(
                                lbl(
                                    "VAS_045_Drafted",
                                    "Drafted"
                                )
                            ) +

                            " " +

                            esc(
                                journal.CreatedDate
                            )
                        )
                        : ""
                ) +

                "</div>" +

                "</div>";

            if ($detailBody) {
                $detailBody.html(html);
            }

            detailLoaded = true;

            updateActionButtons();
        }

        function updateActionButtons() {
            var status =
                String(
                    selectedJournalStatus ||
                    ""
                ).toUpperCase();

            var canPost =
                detailLoaded &&
                !selectedJournalPosted &&
                (
                    status === "AP" ||
                    status === "CO" ||
                    status === "CL"
                );

            if ($postButton) {
                $postButton
                    .toggle(canPost)
                    .prop(
                        "disabled",
                        journalActionInProgress ||
                        !canPost ||
                        selectedJournalId <= 0
                    );
            }

            if ($detailCloseButton) {
                $detailCloseButton.prop(
                    "disabled",
                    journalActionInProgress
                );
            }
        }

        function executeJournalAction(
            actionName,
            actionType
        ) {
            if (
                journalActionInProgress ||
                selectedJournalId <= 0 ||
                !detailLoaded
            ) {
                return;
            }

            var status =
                String(
                    selectedJournalStatus ||
                    ""
                ).toUpperCase();

            if (
                actionType === "approve" &&
                status !== "DR" &&
                status !== "IP" &&
                status !== "NA"
            ) {
                return;
            }

            if (
                actionType === "post" &&
                status !== "AP" &&
                status !== "CO" &&
                status !== "CL"
            ) {
                return;
            }

            journalActionInProgress =
                true;

            setActionBusy(
                true,
                actionType
            );

            actionRequest =
                $.ajax({
                    url:
                        baseUrl +
                        "VAS/VAS_045_GLJournalPendingWidget/" +
                        actionName,

                    type:
                        "POST",

                    dataType:
                        "json",

                    cache:
                        false,

                    data: {
                        journalId:
                            selectedJournalId
                    },

                    success:
                        function (result) {
                            var data =
                                normalizeResponse(
                                    result
                                );

                            if (
                                !data ||
                                data.success === false ||
                                data.error
                            ) {
                                showProcessError(
                                    getResponseMessage(
                                        data,
                                        lbl(
                                            "VAS_045_JournalProcessFailed",
                                            "Journal Process Failed."
                                        )
                                    )
                                );

                                return;
                            }

                            selectedJournalStatus =
                                String(
                                    data.docStatus ||
                                    selectedJournalStatus
                                ).toUpperCase();

                            selectedJournalPosted =
                                isPosted(
                                    data.posted
                                );

                            /*
                             * Refresh queue after status changes.
                             * Records no longer pending disappear automatically.
                             */
                            loadData();

                            if (
                                selectedJournalId > 0 &&
                                $detailDialog &&
                                $detailDialog.is(
                                    ":visible"
                                )
                            ) {
                                loadJournalDetail(
                                    selectedJournalId
                                );
                            }
                        },

                    error:
                        function (
                            xhr,
                            textStatus
                        ) {
                            if (
                                textStatus === "abort"
                            ) {
                                return;
                            }

                            showProcessError(
                                getAjaxErrorMessage(
                                    xhr,
                                    lbl(
                                        "VAS_045_JournalProcessFailed",
                                        "Journal Process Failed."
                                    )
                                )
                            );
                        },

                    complete:
                        function () {
                            actionRequest =
                                null;

                            journalActionInProgress =
                                false;

                            setActionBusy(
                                false,
                                actionType
                            );
                        }
                });
        }

        function setActionBusy(
            busy,
            actionType
        ) {
            showDetailBusy(busy);

            if ($postButton) {
                $postButton.text(
                    busy &&
                        actionType === "post"
                        ? lbl(
                            "VAS_045_Posting",
                            "Posting..."
                        )
                        : lbl(
                            "VAS_045_PostJournal",
                            "Post Journal"
                        )
                );
            }

            updateActionButtons();
        }

        function renderDetailError(message) {
            var text =
                message ||

                lbl(
                    "VIS_Error",
                    "Error Loading Data."
                );

            detailLoaded =
                false;

            selectedJournalStatus =
                "";

            selectedJournalPosted =
                false;

            if ($detailBody) {
                $detailBody.html(
                    '<div class="VAS-gljpq-dialog-empty">' +

                    esc(text) +

                    "</div>"
                );
            }

            updateActionButtons();
        }

        function initials(name) {
            var parts =
                String(
                    name || ""
                )
                    .trim()
                    .split(/\s+/);

            if (
                !parts.length ||
                !parts[0]
            ) {
                return "--";
            }

            if (parts.length === 1) {
                return parts[0]
                    .charAt(0)
                    .toUpperCase();
            }

            return (
                parts[0].charAt(0) +
                parts[1].charAt(0)
            ).toUpperCase();
        }

        function showProcessError(message) {
            window.alert(
                message ||

                lbl(
                    "VAS_045_JournalProcessFailed",
                    "Journal Process Failed."
                )
            );
        }

        function showError(message) {
            if (
                !$root ||
                !$root.length
            ) {
                return;
            }

            var id =
                $self.AD_UserHomeWidgetID;

            var text =
                message ||

                lbl(
                    "VIS_Error",
                    "Error Loading Data."
                );

            currentData =
                null;

            pageNo =
                1;

            totalPages =
                0;

            totalCount =
                0;

            $root.find(
                "#VAS-gljpq-body-" +
                id
            )
                .addClass(
                    "is-empty"
                )
                .html(
                    '<div class="VAS-gljpq-empty">' +

                    esc(text) +

                    "</div>"
                );

            $root.find(
                "#VAS-gljpq-count-" +
                id
            ).text(
                "0 " +

                lbl(
                    "VAS_045_Items",
                    "Items"
                )
            );

            updatePager();
        }

        function refreshData() {
            if (isDisposed) {
                return;
            }

            loadData();

            if (
                $detailDialog &&
                $detailDialog.is(
                    ":visible"
                ) &&
                selectedJournalId > 0
            ) {
                loadJournalDetail(
                    selectedJournalId
                );
            }
        }

        this.refreshWidget =
            refreshData;

        this.getRoot =
            function () {
                return $root;
            };

        this.disposeComponent =
            function () {
                isDisposed =
                    true;

                if (refreshTimer) {
                    window.clearInterval(
                        refreshTimer
                    );

                    refreshTimer =
                        null;
                }

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

                if (
                    detailRequest &&
                    detailRequest.readyState !== 4
                ) {
                    detailRequest.abort();
                }

                if (
                    actionRequest &&
                    actionRequest.readyState !== 4
                ) {
                    actionRequest.abort();
                }

                if (
                    $self &&
                    $self.AD_UserHomeWidgetID
                ) {
                    $(document).off(
                        "keydown.VAS-gljpq-" +
                        $self.AD_UserHomeWidgetID
                    );
                }

                $("body").removeClass(
                    "VAS-gljpq-body-lock"
                );

                if ($detailDialog) {
                    $detailDialog.off();

                    $detailDialog.remove();

                    $detailDialog =
                        null;
                }

                if ($root) {
                    $root.off();

                    $root.remove();
                }

                $detailBody =
                    null;

                $detailBusy =
                    null;

                $postButton =
                    null;

                $detailCloseButton =
                    null;

                currentData =
                    null;

                selectedJournalId =
                    0;

                selectedJournalStatus =
                    "";

                selectedJournalPosted =
                    false;

                detailLoaded =
                    false;

                $root =
                    null;

                $self =
                    null;
            };
    };

    VAS.VAS_045_GLJournalPendingWidget
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
        };

    VAS.VAS_045_GLJournalPendingWidget
        .prototype.widgetSizeChange =
        function (
            height,
            width
        ) {
        };

    VAS.VAS_045_GLJournalPendingWidget
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
