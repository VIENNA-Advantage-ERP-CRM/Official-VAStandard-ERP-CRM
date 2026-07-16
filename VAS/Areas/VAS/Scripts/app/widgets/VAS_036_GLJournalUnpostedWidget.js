/**
 * GL Journal Unposted KPI Widget
 * Purpose - Display unposted GL journals and journal details.
 *
 * -- Labels / Message Keys --------------------------------------------
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Unposted                             | VAS_036_GLJUnposted
 *  2  | Drafts waiting                       | VAS_036_DraftsWaiting
 *  3  | Unposted Journals                    | VAS_036_UnpostedJournals
 *  4  | Journals waiting to be posted        | VAS_036_UnpostedSub
 *  5  | Export                               | VAS_Export
 *  6  | Close                                | VAS_Close
 *  7  | Approve                              | VAS_036_Approve
 *  8  | Post Journal                         | VAS_036_PostJournal
 *  9  | Error Loading Data                   | VIS_Error
 * 10  | No Data                              | VIS_NoData
 * 11  | No Journal Lines                     | VAS_036_NoJournalLines
 * ---------------------------------------------------------------------
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
        var text = VIS.Msg.getMsg(key);

        return (
            text &&
            text.charAt(0) !== "["
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
            if (typeof data !== "string") {
                break;
            }

            try {
                data = JSON.parse(data);
            }
            catch (error) {
                return null;
            }
        }

        return data;
    }

    function formatAmount(
        amount,
        precision
    ) {
        var standardPrecision = 2;

        try {
            standardPrecision =
                Number(
                    VIS.Env
                        .getCtx()
                        .getStdPrecision()
                );
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

    function isPosted(value) {
        var text =
            String(
                value == null
                    ? ""
                    : value
            ).toUpperCase();

        return (
            value === true ||
            value === 1 ||
            text === "Y" ||
            text === "TRUE" ||
            text === "1"
        );
    }

    var PILL_CLASS = {
        "DR": "VAS-glju-pill-draft",
        "CO": "VAS-glju-pill-posted",
        "CL": "VAS-glju-pill-posted",
        "IP": "VAS-glju-pill-submit",
        "AP": "VAS-glju-pill-posted",
        "NA": "VAS-glju-pill-pending",
        "VO": "VAS-glju-pill-voided",
        "RE": "VAS-glju-pill-returned"
    };

    VAS.VAS_036_GLJournalUnpostedWidget =
        function () {
            this.frame = null;
            this.windowNo = 0;
            this.AD_UserHomeWidgetID = 0;

            var $self = this;

            var $root =
                $('<div class="VAS-glju-root">');

            var $kpiValue = null;
            var $whyText = null;

            var $dialog = null;
            var $dialogBody = null;
            var $dialogFooterText = null;
            var $dialogBusy = null;

            var dialogLoaded = false;

            var dialogPageNo = 1;
            var dialogPageSize = 9;
            var dialogTotalPages = 1;
            var dialogTotalCount = 0;

            var countRequest = null;
            var listRequest = null;

            var isDisposed = false;

            var baseUrl =
                VIS.Application.contextUrl;

            this.Initalize = function () {
                createWidget();
                createBusyIndicator();

                showBusy(true);
                loadData();
            };

            function docIconSvg() {
                return (
                    '<svg viewBox="0 0 24 24" ' +
                    'fill="none" ' +
                    'stroke="currentColor" ' +
                    'stroke-width="2" ' +
                    'stroke-linecap="round" ' +
                    'stroke-linejoin="round">' +

                    '<path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z"></path>' +
                    '<polyline points="14 2 14 8 20 8"></polyline>' +
                    '<path d="M16 13H8M16 17H8"></path>' +

                    "</svg>"
                );
            }

            function resolveTotalCount(totalCount, visibleCount, pageNo, pageSize, totalPages) {
                var total = Number(totalCount || 0);

                if (!isNaN(total) && total > 0) {
                    return total;
                }

                var rows = Number(visibleCount || 0);

                if (isNaN(rows) || rows <= 0) {
                    return 0;
                }

                var size = Math.max(parseInt(pageSize || rows || 1, 10), 1);
                var pages = Math.max(parseInt(totalPages || 1, 10), 1);

                if (pages > 1) {
                    return Math.max(((pages - 1) * size) + rows, rows);
                }

                return rows;
            }

            function formatRangeText(pageNo, pageSize, totalCount) {
                var total = Number(totalCount || 0);

                if (isNaN(total) || total <= 0) {
                    return "";
                }

                var page = Math.max(parseInt(pageNo || 1, 10), 1);
                var size = Math.max(parseInt(pageSize || total, 10), 1);
                var start = ((page - 1) * size) + 1;

                if (start > total) {
                    start = total;
                }

                var end = Math.min(start + size - 1, total);

                return lbl("VAS_Showing", "Showing") +
                    " " +
                    start +
                    "-" +
                    end +
                    " " +
                    lbl("VIS_Of", "of") +
                    " " +
                    total;
            }

            function createBusyIndicator() {
                var id =
                    $self.AD_UserHomeWidgetID;

                var $busy =
                    $(
                        '<div id="VAS-glju-busy-' +
                        id +
                        '" class="vis-busyindicatorouterwrap">' +

                        '<div class="vis-busyindicatorinnerwrap">' +
                        '<i class="vis_widgetloader"></i>' +
                        "</div>" +

                        "</div>"
                    );

                $root.append($busy);
            }

            function showBusy(show) {
                $root.find(
                    "#VAS-glju-busy-" +
                    $self.AD_UserHomeWidgetID
                ).toggle(show);
            }

            function showDialogBusy(show) {
                if (
                    $dialogBusy &&
                    $dialogBusy[0]
                ) {
                    $dialogBusy[0]
                        .style.visibility =
                        show
                            ? "visible"
                            : "hidden";
                }
            }

            function createWidget() {
                var id =
                    $self.AD_UserHomeWidgetID;

                var icon =
                    docIconSvg();

                var html =
                    '<div class="kpi kpi-amber" ' +
                    'role="button" tabindex="0">' +

                    '<div class="w-head">' +

                    '<div class="w-icon">' +
                    icon +
                    "</div>" +

                    '<div class="w-title">' +
                    esc(
                        lbl(
                            "VAS_036_GLJUnposted",
                            "Unposted"
                        )
                    ) +
                    "</div>" +

                    "</div>" +

                    '<div class="kpi-value warning" ' +
                    'id="VAS-glju-val-' +
                    id +
                    '">&mdash;</div>' +

                    '<div class="kpi-why">' +
                    '<span class="kpi-why-text" ' +
                    'id="VAS-glju-why-' +
                    id +
                    '">' +

                    esc(
                        lbl(
                            "VAS_036_DraftsWaiting",
                            "Drafts waiting to be approved + posted."
                        )
                    ) +

                    "</span>" +
                    "</div>" +

                    "</div>";

                $root.append(html);

                $kpiValue =
                    $root.find(
                        "#VAS-glju-val-" +
                        id
                    );

                $whyText =
                    $root.find(
                        "#VAS-glju-why-" +
                        id
                    );

                $root.find(".kpi")
                    .on(
                        "click",
                        openDialog
                    )
                    .on(
                        "keydown",
                        function (event) {
                            if (
                                event.key === "Enter" ||
                                event.key === " "
                            ) {
                                event.preventDefault();
                                openDialog();
                            }
                        }
                    );

                createDialog(icon);
            }

            function createDialog(icon) {
                var id =
                    $self.AD_UserHomeWidgetID;

                $dialog =
                    $(
                        '<div class="VAS-glju-dialog" ' +
                        'id="VAS-glju-dialog-' +
                        id +
                        '" style="display:none" ' +
                        'role="dialog" aria-modal="true">' +

                        '<div class="VAS-glju-dialog-scrim"></div>' +

                        '<div class="VAS-glju-dialog-card">' +

                        '<div class="VAS-glju-dialog-head">' +

                        '<div class="VAS-glju-dialog-icon">' +
                        icon +
                        "</div>" +

                        '<div class="VAS-glju-dialog-title-wrap">' +

                        '<div class="VAS-glju-dialog-title">' +
                        esc(
                            lbl(
                                "VAS_036_UnpostedJournals",
                                "Unposted Journals"
                            )
                        ) +
                        "</div>" +

                        '<div class="VAS-glju-dialog-sub">' +
                        esc(
                            lbl(
                                "VAS_036_UnpostedSub",
                                "Drafts, submitted and pending approval - not yet posted to GL"
                            )
                        ) +
                        "</div>" +

                        "</div>" +

                        '<button type="button" ' +
                        'class="VAS-glju-dialog-close">' +
                        `<svg viewBox="0 0 24 24"" fill="none"" stroke="currentColor"" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg>` +
                        "</button>" +

                        "</div>" +

                        '<div class="VAS-glju-dialog-body">' +

                        '<div class="VAS-glju-dialog-busy">' +
                        '<div class="vis-busyindicatorinnerwrap">' +
                        '<i class="vis_widgetloader"></i>' +
                        "</div>" +
                        "</div>" +

                        '<div class="VAS-glju-table-wrap" ' +
                        'id="VAS-glju-dialog-body-' +
                        id +
                        '"></div>' +

                        "</div>" +

                        '<div class="VAS-glju-dialog-footer">' +

                        '<span class="VAS-glju-dialog-total" ' +
                        'id="VAS-glju-dialog-total-' +
                        id +
                        '"></span>' +

                        '<div class="VAS-glju-dialog-actions">' +

                        '<button type="button" ' +
                        'class="VAS-glju-export">' +
                        esc(
                            lbl(
                                "VAS_Export",
                                "Export"
                            )
                        ) +
                        "</button>" +

                        '<button type="button" ' +
                        'class="VAS-glju-close-primary">' +
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

                $dialogBody =
                    $dialog.find(
                        "#VAS-glju-dialog-body-" +
                        id
                    );

                $dialogFooterText =
                    $dialog.find(
                        "#VAS-glju-dialog-total-" +
                        id
                    );

                $dialogBusy =
                    $dialog.find(
                        ".VAS-glju-dialog-busy"
                    );

                showDialogBusy(false);

                $dialog.find(
                    ".VAS-glju-dialog-close, " +
                    ".VAS-glju-close-primary, " +
                    ".VAS-glju-dialog-scrim"
                ).on(
                    "click",
                    closeDialog
                );

                $dialog.find(
                    ".VAS-glju-export"
                ).on(
                    "click",
                    exportDialogRows
                );

                /*
                 * Delegated click:
                 * remains active after table HTML is rebuilt.
                 */
                $dialogBody
                    .off(
                        "click.VAS036Detail",
                        ".VAS-glju-entry-row"
                    )
                    .on(
                        "click.VAS036Detail",
                        ".VAS-glju-entry-row",
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

                /*
                 * Clicking the document number zooms to the GL Journal record
                 * (opens the window). stopPropagation keeps the row's detail
                 * popup from also opening.
                 */
                $dialogBody
                    .off(
                        "click.VAS036Zoom",
                        ".VAS-glju-doc-zoom"
                    )
                    .on(
                        "click.VAS036Zoom",
                        ".VAS-glju-doc-zoom",
                        function (event) {
                            event.preventDefault();
                            event.stopPropagation();

                            zoomToJournal(
                                $(this).attr("data-journal-id")
                            );
                        }
                    );

                $dialogBody
                    .off(
                        "keydown.VAS036Zoom",
                        ".VAS-glju-doc-zoom"
                    )
                    .on(
                        "keydown.VAS036Zoom",
                        ".VAS-glju-doc-zoom",
                        function (event) {
                            if (
                                event.key !== "Enter" &&
                                event.key !== " "
                            ) {
                                return;
                            }

                            event.preventDefault();
                            event.stopPropagation();

                            zoomToJournal(
                                $(this).attr("data-journal-id")
                            );
                        }
                    );

                $dialogBody
                    .parent()
                    .off(
                        "click.VAS036DialogPager",
                        ".VAS-glju-dialog-page"
                    )
                    .on(
                        "click.VAS036DialogPager",
                        ".VAS-glju-dialog-page",
                        function (event) {
                            event.preventDefault();
                            event.stopPropagation();

                            var nextPage =
                                $(this).hasClass(
                                    "VAS-glju-dialog-prev"
                                )
                                    ? dialogPageNo - 1
                                    : dialogPageNo + 1;

                            if (
                                nextPage < 1 ||
                                nextPage === dialogPageNo ||
                                nextPage > dialogTotalPages
                            ) {
                                return;
                            }

                            dialogPageNo = nextPage;
                            dialogLoaded = false;
                            loadDialogRows();
                        }
                    );

                $(document).on(
                    "keydown.VAS-glju-" +
                    id,
                    function (event) {
                        if (
                            event.key !== "Escape" ||
                            VAS.GLJournalDetailDialog.isBusy()
                        ) {
                            return;
                        }

                        if (
                            $dialog &&
                            $dialog.is(":visible") &&
                            !VAS.GLJournalDetailDialog.isOpen()
                        ) {
                            closeDialog();
                        }
                    }
                );

                $("body").append($dialog);
            }

            function loadData() {
                if (isDisposed) {
                    return;
                }

                if (
                    countRequest &&
                    countRequest.readyState !== 4
                ) {
                    countRequest.abort();
                }

                showBusy(true);

                countRequest = $.ajax({
                    url:
                        baseUrl +
                        "VAS/VAS_041_GLJournalEntriesWidget/GetUnpostedCount",

                    type: "GET",
                    dataType: "json",
                    cache: false,

                    success: function (result) {
                        var data =
                            normalizeResponse(result);

                        if (
                            data &&
                            !data.error &&
                            data.success !== false
                        ) {
                            $kpiValue.text(
                                Number(
                                    data.UnpostedCount || 0
                                )
                            );

                            return;
                        }

                        $kpiValue.html("&mdash;");

                        $whyText.text(
                            data &&
                                (
                                    data.errorText ||
                                    data.error
                                )
                                ? (
                                    data.errorText ||
                                    data.error
                                )
                                : lbl(
                                    "VIS_Error",
                                    "Error loading data."
                                )
                        );
                    },

                    error: function (
                        xhr,
                        textStatus
                    ) {
                        if (textStatus === "abort") {
                            return;
                        }

                        $kpiValue.html("&mdash;");

                        $whyText.text(
                            getAjaxErrorMessage(xhr)
                        );
                    },

                    complete: function () {
                        countRequest = null;

                        if (!isDisposed) {
                            showBusy(false);
                        }
                    }
                });
            }

            function openDialog() {
                if (
                    !$dialog ||
                    isDisposed
                ) {
                    return;
                }

                $dialog.show();

                $("body").addClass(
                    "VAS-glju-body-lock"
                );

                if (!dialogLoaded) {
                    loadDialogRows();
                }
            }

            function closeDialog() {
                if (VAS.GLJournalDetailDialog.isBusy()) {
                    return;
                }

                VAS.GLJournalDetailDialog.close();

                if ($dialog) {
                    $dialog.hide();
                }

                $("body").removeClass(
                    "VAS-glju-body-lock"
                );
            }

            function openDetailDialog(journalId) {
                VAS.GLJournalDetailDialog.open(journalId, {
                    windowNo: $self.windowNo,
                    onChanged: function () {
                        loadData();

                        if (
                            $dialog &&
                            $dialog.is(":visible")
                        ) {
                            dialogLoaded = false;
                            loadDialogRows();
                        }
                    }
                });
            }

            /*
             * The framework navigates in-place (no new window) only when
             * ActionName matches the window currently hosting this widget;
             * otherwise it opens a new window. Resolve the host window name
             * from the listener chain.
             */
            function hostWindowName() {
                try {
                    var l = $self.listener;

                    for (var i = 0; i < 6 && l; i++) {
                        if (
                            l.apanel &&
                            l.apanel.gridWindow &&
                            l.apanel.gridWindow.getName
                        ) {
                            return l.apanel.gridWindow.getName();
                        }

                        if (l.gridWindow && l.gridWindow.getName) {
                            return l.gridWindow.getName();
                        }

                        l = l.listener;
                    }
                }
                catch (e) { }

                return "";
            }

            /* Zoom to the GL Journal record (opens the GL Journal window). */
            function zoomToJournal(recordId) {
                recordId = parseInt(recordId, 10);

                if (isNaN(recordId) || recordId <= 0) {
                    return;
                }

                try {
                    $self.widgetFirevalueChanged({
                        "TabWhereClause":
                            "GL_Journal.GL_Journal_ID=" + recordId,
                        "TabLayout": "Y",
                        "TabIndex": "0",
                        "ActionName": hostWindowName() || "VAS_GLJournal",
                        "ActionType": "W"
                    });
                }
                catch (e) { /* zoom is best-effort */ }
            }

            function loadDialogRows() {
                if (isDisposed) {
                    return;
                }

                if (
                    listRequest &&
                    listRequest.readyState !== 4
                ) {
                    listRequest.abort();
                }

                showDialogBusy(true);

                listRequest = $.ajax({
                    url:
                        baseUrl +
                        "VAS/VAS_041_GLJournalEntriesWidget/GetUnpostedEntries",

                    type: "GET",
                    data: {
                        pageNo: dialogPageNo,
                        pageSize: dialogPageSize
                    },
                    dataType: "json",
                    cache: false,

                    success: function (result) {
                        var data =
                            normalizeResponse(result);

                        if (
                            data &&
                            !data.error &&
                            data.success !== false
                        ) {
                            renderDialog(data);
                            dialogLoaded = true;
                        }
                        else {
                            renderDialogError(
                                data &&
                                (
                                    data.errorText ||
                                    data.error
                                )
                            );
                        }
                    },

                    error: function (
                        xhr,
                        textStatus
                    ) {
                        if (textStatus === "abort") {
                            return;
                        }

                        renderDialogError(
                            getAjaxErrorMessage(xhr)
                        );
                    },

                    complete: function () {
                        listRequest = null;

                        if (!isDisposed) {
                            showDialogBusy(false);
                        }
                    }
                });
            }

            function renderDialog(data) {
                var rows =
                    Array.isArray(data.Entries)
                        ? data.Entries
                        : [];

                var symbol =
                    data.CurSymbol ||
                    data.ISOCode ||
                    "";

                var precision =
                    Number(data.StdPrecision);

                dialogPageNo =
                    Number(data.PageNo || dialogPageNo || 1);

                dialogPageSize =
                    Number(data.PageSize || dialogPageSize || 9);

                dialogTotalPages =
                    Math.max(
                        1,
                        Number(
                            data.TotalPages ||
                            Math.ceil(
                                Number(
                                    data.TotalCount ||
                                    rows.length ||
                                    0
                                ) / dialogPageSize
                            ) ||
                            1
                        )
                    );

                dialogTotalCount =
                    resolveTotalCount(
                        data.TotalCount,
                        rows.length,
                        dialogPageNo,
                        dialogPageSize,
                        dialogTotalPages
                    );

                if (!rows.length) {
                    $dialogBody.html(
                        '<div class="VAS-glju-dialog-empty">' +
                        esc(
                            lbl(
                                "VIS_NoData",
                                "No data available."
                            )
                        ) +
                        "</div>"
                    );

                    $dialogBody
                        .siblings(
                            ".VAS-glju-dialog-pager"
                        )
                        .remove();

                    $dialogFooterText.text("");
                    return;
                }

                var html =
                    '<table class="VAS-glju-dialog-table">' +
                    "<thead><tr>" +

                    "<th>" + esc(lbl("VAS_036_JournalNo", "Journal No.")) + "</th>" +
                    "<th>" + esc(lbl("VAS_Date", "Date")) + "</th>" +
                    "<th>" + esc(lbl("Description", "Description")) + "</th>" +
                    "<th>" + esc(lbl("Status", "Status")) + "</th>" +
                    "<th>" + esc(lbl("VAS_036_TotalDebit", "Total Debit")) + "</th>" +
                    "<th>" + esc(lbl("VAS_036_TotalCredit", "Total Credit")) + "</th>" +

                    "</tr></thead><tbody>";

                for (
                    var index = 0;
                    index < rows.length;
                    index++
                ) {
                    var row = rows[index];

                    var status =
                        row.StatusName ||
                        row.DocStatus ||
                        "";

                    var pillClass =
                        PILL_CLASS[row.DocStatus] ||
                        "VAS-glju-pill-draft";

                    html +=
                        '<tr class="VAS-glju-entry-row" ' +
                        'role="button" tabindex="0" ' +
                        'data-journal-id="' +
                        esc(row.GL_Journal_ID) +
                        '">' +

                        '<td class="VAS-glju-doc">' +
                        '<span class="VAS-glju-doc-zoom" data-journal-id="' +
                        esc(row.GL_Journal_ID) +
                        '" role="link" tabindex="0" title="' +
                        esc(lbl("VAS_036_OpenRecord", "Open record")) +
                        '">' +
                        esc(row.DocumentNo) +
                        "</span>" +
                        "</td>" +

                        '<td class="VAS-glju-date">' +
                        esc(row.DateAcct) +
                        "</td>" +

                        '<td class="VAS-glju-desc">' +
                        esc(row.Description) +
                        "</td>" +

                        "<td>" +
                        '<span class="VAS-glju-pill ' +
                        pillClass +
                        '">' +
                        "<span></span>" +
                        esc(status) +
                        "</span>" +
                        "</td>" +

                        '<td class="VAS-glju-amt" title="' + esc(symbol + formatAmount(row.TotalDebit, precision)) + '">' +
                        esc(symbol + formatAmount(row.TotalDebit, precision)) +
                        "</td>" +

                        '<td class="VAS-glju-amt" title="' + esc(symbol + formatAmount(row.TotalCredit, precision)) + '">' +
                        esc(symbol + formatAmount(row.TotalCredit, precision)) +
                        "</td>" +

                        "</tr>";
                }

                html +=
                    "</tbody></table>";

                $dialogBody.html(html);

                $dialogBody
                    .siblings(
                        ".VAS-glju-dialog-pager"
                    )
                    .remove();

                $dialogBody.after(
                    renderDialogPager()
                );

                $dialogFooterText.text(
                    rows.length +
                    " " +
                    lbl("VAS_036_Journals", "journals") +
                    " \u00B7 " +
                    lbl("Total", "total") +
                    " " +
                    symbol +
                    formatAmount(
                        data.TotalDebit,
                        precision
                    )
                );
            }

            function renderDialogPager() {
                return '<div class="VAS-glju-line-pager VAS-glju-dialog-pager">' +
                    '<span class="VAS-glju-page-text">' +
                    esc(formatRangeText(dialogPageNo, dialogPageSize, dialogTotalCount)) +
                    '</span>' +
                    '<button type="button" class="VAS-glju-page-btn VAS-glju-dialog-page VAS-glju-dialog-prev" ' +
                    (dialogPageNo <= 1 || dialogTotalPages <= 1 ? "disabled " : "") +
                    'aria-label="' + esc(lbl("VIS_Previous", "Previous")) + '">&#8249;</button>' +
                    '<span class="VAS-glju-page-count">' +
                    esc(dialogPageNo + " " + lbl("VIS_Of", "of") + " " + dialogTotalPages) +
                    '</span>' +
                    '<button type="button" class="VAS-glju-page-btn VAS-glju-dialog-page VAS-glju-dialog-next" ' +
                    (dialogPageNo >= dialogTotalPages || dialogTotalPages <= 1 ? "disabled " : "") +
                    'aria-label="' + esc(lbl("VIS_Next", "Next")) + '">&#8250;</button>' +
                    '</div>';
            }

            function getAjaxErrorMessage(xhr) {
                var response =
                    normalizeResponse(
                        xhr &&
                        xhr.responseText
                    );

                if (response) {
                    return (
                        response.errorKey
                            ? lbl(
                                response.errorKey,
                                response.errorText ||
                                response.error ||
                                response.message ||
                                lbl("VIS_Error", "Error loading data.")
                            )
                            : (
                                response.errorText ||
                                response.error ||
                                response.message ||
                                lbl("VIS_Error", "Error loading data.")
                            )
                    );
                }

                return xhr && xhr.status
                    ? (
                        lbl("VAS_036_RequestFailedHttp", "Request failed. HTTP {0}")
                            .replace(
                                "{0}",
                                xhr.status
                            )
                    )
                    : lbl(
                        "VIS_Error",
                        "Error loading data."
                    );
            }

            function renderDialogError(message) {
                dialogLoaded = false;

                $dialogBody.html(
                    '<div class="VAS-glju-dialog-empty">' +
                    esc(
                        message ||
                        lbl(
                            "VIS_Error",
                            "Error loading data."
                        )
                    ) +
                    "</div>"
                );

                $dialogFooterText.text("");
            }

            function exportDialogRows() {
                var $table =
                    $dialogBody.find(
                        ".VAS-glju-dialog-table"
                    );

                if (!$table.length) {
                    return;
                }

                var html =
                    '<html xmlns:o="urn:schemas-microsoft-com:office:office" ' +
                    'xmlns:x="urn:schemas-microsoft-com:office:excel" ' +
                    'xmlns="http://www.w3.org/TR/REC-html40">' +
                    '<head><meta charset="utf-8"></head>' +
                    "<body>" +
                    $table[0].outerHTML +
                    "</body></html>";

                var blob =
                    new Blob(
                        [html],
                        {
                            type:
                                "application/vnd.ms-excel;charset=utf-8;"
                        }
                    );

                var url =
                    URL.createObjectURL(blob);

                var link =
                    document.createElement("a");

                link.href = url;
                link.download =
                    "unposted-journals.xls";

                document.body.appendChild(link);
                link.click();
                document.body.removeChild(link);

                URL.revokeObjectURL(url);
            }

            function refreshData() {
                dialogLoaded = false;

                $kpiValue.html("&mdash;");

                loadData();

                if (
                    $dialog &&
                    $dialog.is(":visible")
                ) {
                    loadDialogRows();
                }
            }

            this.refreshData =
                refreshData;

            this.getRoot =
                function () {
                    return $root;
                };

            this.disposeComponent =
                function () {
                    isDisposed = true;

                    if (
                        countRequest &&
                        countRequest.readyState !== 4
                    ) {
                        countRequest.abort();
                    }

                    if (
                        listRequest &&
                        listRequest.readyState !== 4
                    ) {
                        listRequest.abort();
                    }

                    $(document).off(
                        "keydown.VAS-glju-" +
                        $self.AD_UserHomeWidgetID
                    );

                    $("body").removeClass(
                        "VAS-glju-body-lock"
                    );

                    if ($dialog) {
                        $dialog.remove();
                    }

                    $root.remove();
                };
        };

    VAS.VAS_036_GLJournalUnpostedWidget
        .prototype.refreshWidget =
        function () {
            if (
                typeof this.refreshData ===
                "function"
            ) {
                this.refreshData();
            }
        };

    VAS.VAS_036_GLJournalUnpostedWidget
        .prototype.init =
        function (
            windowNo,
            frame
        ) {
            this.frame = frame;

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

    VAS.VAS_036_GLJournalUnpostedWidget
        .prototype.widgetSizeChange =
        function () {
        };

    /* The widget host registers itself here so the widget can drive the host
       (in-place tab navigation / zoom). */
    VAS.VAS_036_GLJournalUnpostedWidget
        .prototype.addChangeListener =
        function (listener) {
            this.listener = listener;
        };

    /* Relay a fired value (e.g. zoom TabWhereClause) to the registered host. */
    VAS.VAS_036_GLJournalUnpostedWidget
        .prototype.widgetFirevalueChanged =
        function (value) {
            if (this.listener) {
                this.listener.widgetFirevalueChanged(value);
            }
        };

    VAS.VAS_036_GLJournalUnpostedWidget
        .prototype.dispose =
        function () {
            this.disposeComponent();

            if (this.frame) {
                this.frame.dispose();
            }

            this.frame = null;
        };

})(VAS, jQuery);
