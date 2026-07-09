/**
 * QA Holds Widget (Material Receipt / GRN dashboard)
 * Purpose - 3x2 glass list of GRN receipt confirmation lines currently held
 *           for quality inspection. Row click opens the Quality Control form
 *           and Save QA Result posts the actual value, QA/QC date and note.
 * Backend - VAS_086_QAHoldsWidget/GetQAHolds
 *           VAS_086_QAHoldsWidget/SaveQAResult
 * Summary Message Table: see Labels / Message Keys below.
 *
 * Labels / Message Keys
 *  #  | Current Text            | Message Key
 * ----+-------------------------+---------------------------
 *  1  | QA Holds                | VAS_086_QAHolds
 *  2  | Tap a receipt...        | VAS_086_QAHoldsSubtitle
 *  3  | Quality Control         | VAS_086_QualityControl
 *  4  | Quality check           | VAS_086_QualityCheck
 *  5  | Actual Value            | VAS_086_ActualValue
 *  6  | Working Fine            | VAS_086_WorkingFine
 *  7  | Not Satisfactory        | VAS_086_NotSatisfactory
 *  8  | QA/QC Date              | VAS_086_QAQCDate
 *  9  | Description             | VAS_086_Description
 * 10  | Save QA Result          | VAS_086_SaveQAResult
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

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

    VAS.VAS_086_QAHoldsWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="vas-qah-root">');
        var $listBody;
        var $busy;
        var $footer, $pageInfo, $pageText, $prevBtn, $nextBtn;
        var $dialog, $dialogBody, $dialogTitle, $dialogStatus, $dialogBusy;

        var rows = [];
        var rowsById = {};
        var activeRecord = null;
        var pageNo = 1;
        var pageSize = 5;
        var totalPages = 0;
        var totalRecords = 0;
        var loading = false;
        var rowResizeObserver = null;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function escapeHtml(value) {
            return String(value == null ? "" : value)
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;")
                .replace(/'/g, "&#039;");
        }

        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data;
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-qah-hidden', !show);
        }

        function showDialogBusy(show) {
            if (!$dialogBusy || !$dialogBusy[0]) { return; }
            $dialogBusy.toggleClass('vas-qah-hidden', !show);
        }

        function icon(name) {
            if (name === "shield") {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/></svg>';
            }
            if (name === "file") {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><path d="M14 2v6h6"/></svg>';
            }
            if (name === "scale") {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3v18"/><path d="M5 7h14"/><path d="M6 7l-3 6h6L6 7z"/><path d="M18 7l-3 6h6l-3-6z"/></svg>';
            }
            if (name === "clipboard") {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M9 4h6"/><path d="M9 2h6v4H9z"/><path d="M8 4H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V6a2 2 0 0 0-2-2h-2"/></svg>';
            }
            if (name === "package") {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M3 7l9-4 9 4-9 4-9-4z"/><path d="M3 7v10l9 4 9-4V7"/><path d="M12 11v10"/></svg>';
            }
            if (name === "calendar") {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="18" rx="2"/><path d="M16 2v4"/><path d="M8 2v4"/><path d="M3 10h18"/></svg>';
            }
            if (name === "edit") {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h9"/><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4 12.5-12.5z"/></svg>';
            }
            if (name === "check") {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.1" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6L9 17l-5-5"/></svg>';
            }
            if (name === "chevL") {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>';
            }
            if (name === "chevR") {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>';
            }
            if (name === "chevD") {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.1" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"/></svg>';
            }
            if (name === "kebab") {
                return '<svg viewBox="0 0 24 24" fill="currentColor"><circle cx="12" cy="5" r="1.7"/><circle cx="12" cy="12" r="1.7"/><circle cx="12" cy="19" r="1.7"/></svg>';
            }
            return '<svg viewBox="0 0 24 24"></svg>';
        }

        function formatQty(value) {
            return Number(value || 0).toLocaleString(window.navigator.language, { maximumFractionDigits: 2 });
        }

        function todayYmd() {
            var d = new Date();
            var month = d.getMonth() + 1;
            var day = d.getDate();
            return d.getFullYear() + '-' + (month < 10 ? '0' + month : month) + '-' + (day < 10 ? '0' + day : day);
        }

        function holdAge(value) {
            if (!value) { return "-"; }
            var d = new Date(value);
            if (isNaN(d.getTime())) { return "-"; }
            var diffHours = Math.max(0, Math.floor((new Date().getTime() - d.getTime()) / 3600000));
            if (diffHours < 24) { return diffHours + "h"; }
            return Math.floor(diffHours / 24) + "d";
        }

        function lineReference(record) {
            if (record.confirmationNo) { return record.confirmationNo; }
            var line = record.lineNo ? record.lineNo + " - " : "";
            return line + (record.itemName || "-") + " - " + (record.grnNo || "-");
        }

        function measurePageSize() {
            if (!$listBody || !$listBody[0]) { return pageSize; }

            var listHeight = $listBody.innerHeight();
            var rowHeight = $listBody.find('.vas-qah-row').first().outerHeight(true) || 44;
            if (!listHeight || !rowHeight) { return pageSize; }

            return Math.max(3, Math.floor(listHeight / rowHeight));
        }

        function syncPageSize() {
            var nextPageSize = measurePageSize();
            if (nextPageSize === pageSize) { return; }

            var firstRecord = ((pageNo - 1) * pageSize) + 1;
            pageSize = nextPageSize;
            pageNo = Math.max(1, Math.ceil(firstRecord / pageSize));
            if (!loading) { loadPage(pageNo); }
        }

        this.Initalize = function () {
            createWidget();
            createDialog();
            loadPage(1);
        };

        function createWidget() {
            var $card = $('<div class="vas-qah-card vas-widget-bg">');
            var $header = $(
                '<div class="vas-qah-head">' +
                '<span class="vas-qah-ico">' + icon("shield") + '</span>' +
                '<div class="vas-qah-titles">' +
                '<div class="vas-qah-title">' + escapeHtml(lbl("VAS_086_QAHolds", "QA Holds")) + '</div>' +
                '<div class="vas-qah-sub">' + escapeHtml(lbl("VAS_086_QAHoldsSubtitle", "Tap a receipt to verify quality")) + '</div>' +
                '</div>' +
                '</div>'
            );

            $listBody = $('<div class="vas-qah-list">');
            $footer = $(
                '<div class="vas-qah-foot vas-qah-hidden">' +
                '<span class="vas-qah-foot-info"></span>' +
                '<div class="vas-qah-pager">' +
                '<button type="button" class="vas-qah-pgbtn vas-qah-prev" aria-label="' + escapeHtml(lbl("VAS_086_Previous", "Previous")) + '">' + icon("chevL") + '</button>' +
                '<span class="vas-qah-pgtext"></span>' +
                '<button type="button" class="vas-qah-pgbtn vas-qah-next" aria-label="' + escapeHtml(lbl("VAS_086_Next", "Next")) + '">' + icon("chevR") + '</button>' +
                '</div>' +
                '</div>'
            );

            $pageInfo = $footer.find('.vas-qah-foot-info');
            $pageText = $footer.find('.vas-qah-pgtext');
            $prevBtn = $footer.find('.vas-qah-prev');
            $nextBtn = $footer.find('.vas-qah-next');

            $card.append($header).append($listBody).append($footer);
            $root.append($card);

            $busy = $('<div class="vas-qah-busy vas-qah-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);

            $listBody.on('click', '.vas-qah-row', function () {
                openQADetail($(this).data('holdid'));
            });
            $prevBtn.on('click', function () { if (!loading && pageNo > 1) { loadPage(pageNo - 1); } });
            $nextBtn.on('click', function () { if (!loading && pageNo < totalPages) { loadPage(pageNo + 1); } });

            if (window.ResizeObserver) {
                rowResizeObserver = new ResizeObserver(function () {
                    window.setTimeout(syncPageSize, 0);
                });
                rowResizeObserver.observe($listBody[0]);
            }
        }

        function loadPage(page) {
            loading = true;
            showBusy(true);
            if ($prevBtn) { $prevBtn.prop("disabled", true); }
            if ($nextBtn) { $nextBtn.prop("disabled", true); }

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_086_QAHoldsWidget/GetQAHolds',
                type: 'GET',
                cache: false,
                data: { pageNo: page, pageSize: pageSize },
                success: function (res) {
                    var data = parseResponse(res);
                    loading = false;
                    showBusy(false);

                    if (!data || data.error) {
                        setEmpty();
                        return;
                    }

                    rows = data.rows || [];
                    rowsById = {};
                    for (var i = 0; i < rows.length; i++) {
                        rowsById[rows[i].grnConfirmationLineId] = rows[i];
                    }

                    pageNo = Number(data.pageNo || page);
                    totalPages = Number(data.totalPages || 0);
                    totalRecords = Number(data.totalRecords || 0);
                    renderRows(data.missingSchema ? data.message : "");
                    updatePager();
                    window.setTimeout(syncPageSize, 0);
                },
                error: function () {
                    loading = false;
                    showBusy(false);
                    setEmpty();
                }
            });
        }

        function setEmpty() {
            rows = [];
            rowsById = {};
            totalRecords = 0;
            totalPages = 0;
            renderRows("");
            updatePager();
            window.setTimeout(syncPageSize, 0);
        }

        function renderRows(message) {
            $listBody.empty();

            if (!rows || rows.length === 0) {
                $listBody.append('<div class="vas-qah-empty">' + escapeHtml(message || lbl("VAS_086_NoDataAvailable", "No data available")) + '</div>');
                return;
            }

            for (var i = 0; i < rows.length; i++) {
                var r = rows[i];
                var label = escapeHtml(r.grnNo) + ' &middot; ' + escapeHtml(r.supplier);
                var meta = escapeHtml(r.itemName) + ' &middot; ' + escapeHtml(lbl("VAS_086_Held", "held")) + ' ' + escapeHtml(holdAge(r.holdStartedOn));
                $listBody.append(
                    '<button type="button" class="vas-qah-row" data-holdid="' + escapeHtml(r.grnConfirmationLineId) + '">' +
                    '<div class="vas-qah-row-main">' +
                    '<div class="vas-qah-row-label" title="' + escapeHtml((r.grnNo || "") + " - " + (r.supplier || "")) + '">' + label + '</div>' +
                    '<div class="vas-qah-row-meta">' + meta + '</div>' +
                    '</div>' +
                    '<div class="vas-qah-row-qty" title="' + escapeHtml(formatQty(r.heldQty)) + '">' + escapeHtml(formatQty(r.heldQty)) + ' u</div>' +
                    '</button>'
                );
            }
        }

        function updatePager() {
            if ($pageText) {
                $pageText.text(totalPages > 1 ? (pageNo + ' ' + lbl("VAS_086_Of", "of") + ' ' + totalPages) : '');
            }
            if ($pageInfo) {
                if (totalRecords > 0 && totalPages > 1) {
                    var from = (pageNo - 1) * pageSize + 1;
                    var to = Math.min(pageNo * pageSize, totalRecords);
                    $pageInfo.text(lbl("VAS_086_Showing", "Showing") + ' ' + from + '-' + to + ' ' + lbl("VAS_086_Of", "of") + ' ' + totalRecords);
                } else {
                    $pageInfo.text('');
                }
            }
            if ($prevBtn) { $prevBtn.prop('disabled', loading || pageNo <= 1); }
            if ($nextBtn) { $nextBtn.prop('disabled', loading || totalPages <= 1 || pageNo >= totalPages); }
            if ($footer) { $footer.toggleClass('vas-qah-hidden', totalPages <= 1); }
        }

        function createDialog() {
            $dialog = $(
                '<div class="vas-qah-dialog vas-qah-hidden" role="dialog" aria-modal="true">' +
                '<div class="vas-qah-scrim"></div>' +
                '<div class="vas-qah-modal">' +
                '<div class="vas-qah-modal-head">' +
                '<div class="vas-qah-modal-title-wrap">' +
                '<h3 class="vas-qah-modal-title"></h3>' +
                '<span class="vas-qah-modal-status"></span>' +
                '</div>' +
                '<button type="button" class="vas-qah-modal-close" aria-label="' + escapeHtml(lbl("VAS_086_Close", "Close")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>' +
                '</button>' +
                '</div>' +
                '<div class="vas-qah-modal-body"></div>' +
                '<div class="vas-qah-modal-busy vas-qah-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '</div>' +
                '</div>'
            );

            $dialogBody = $dialog.find('.vas-qah-modal-body');
            $dialogTitle = $dialog.find('.vas-qah-modal-title');
            $dialogStatus = $dialog.find('.vas-qah-modal-status');
            $dialogBusy = $dialog.find('.vas-qah-modal-busy');

            $dialog.find('.vas-qah-modal-close').on('click', closeDetail);
            $dialog.find('.vas-qah-scrim').on('click', closeDetail);
            $dialog.on('change', '.vas-qah-actual', updateActualTone);
            $dialog.on('click', '.vas-qah-save', saveQAResult);
            $(document).on('keydown.vas-qah', function (e) {
                if (e.key === 'Escape' && !$dialog.hasClass('vas-qah-hidden')) { closeDetail(); }
            });

            $('body').append($dialog);
        }

        function disabledField(iconName, label, value, right) {
            var shown = value == null || value === "" ? "-" : value;
            return '<div class="vas-qah-qc-field disabled">' +
                '<div class="vas-qah-qc-ico">' + icon(iconName) + '</div>' +
                '<div class="vas-qah-qc-main">' +
                '<div class="vas-qah-qc-label">' + escapeHtml(label) + '</div>' +
                '<div class="vas-qah-qc-value' + (right ? ' right' : '') + '" title="' + escapeHtml(shown) + '">' + escapeHtml(shown) + '</div>' +
                '</div>' +
                '</div>';
        }

        function openQADetail(holdId) {
            var record = rowsById[holdId];
            if (!record || !$dialog) { return; }

            activeRecord = record;
            $dialogTitle.text((record.itemName || lbl("VAS_086_QualityControl", "Quality Control")) + ' - QA');
            $dialogStatus.html('<span class="vas-qah-pill-warn">' + escapeHtml(lbl("VAS_086_QualityCheck", "Quality check")) + '</span>');
            renderQADetail(record);
            $dialog.removeClass('vas-qah-hidden');
            $('body').addClass('vas-qah-body-lock');
        }

        function renderQADetail(record) {
            var actual = record.actualValue || "";
            var qaDate = record.qaQcDate || todayYmd();
            var saveDisabled = record.qaRecordId > 0 ? "" : " disabled";
            var saveTitle = record.qaRecordId > 0 ? "" : ' title="' + escapeHtml(lbl("VAS_086_QARecordMissing", "QA inspection record is missing.")) + '"';

            var left =
                disabledField("file", lbl("VAS_086_GRNConfirmationLine", "GRN Confirmation Line"), lineReference(record)) +
                disabledField("scale", lbl("VAS_086_QuantityToVerify", "Quantity To Verify"), formatQty(record.quantityToVerify || record.heldQty), true) +
                disabledField("clipboard", lbl("VAS_086_TestParameter", "Test Parameter"), record.testParameter || record.testParameterId || "-") +
                disabledField("edit", lbl("VAS_086_AcceptableValue", "Acceptable Value"), record.acceptableValue || record.acceptableValueId || "-") +
                '<div class="vas-qah-qc-field active">' +
                '<div class="vas-qah-qc-ico">' + icon("edit") + '</div>' +
                '<div class="vas-qah-qc-main">' +
                '<div class="vas-qah-qc-label req">' + escapeHtml(lbl("VAS_086_ActualValue", "Actual Value")) + '</div>' +
                '<div class="vas-qah-select-wrap">' +
                '<select class="vas-qah-actual">' +
                '<option value="">' + escapeHtml(lbl("VAS_086_SelectResult", "Select result...")) + '</option>' +
                '<option value="Working Fine"' + (actual === "Working Fine" ? " selected" : "") + '>' + escapeHtml(lbl("VAS_086_WorkingFine", "Working Fine")) + '</option>' +
                '<option value="Not Satisfactory"' + (actual === "Not Satisfactory" ? " selected" : "") + '>' + escapeHtml(lbl("VAS_086_NotSatisfactory", "Not Satisfactory")) + '</option>' +
                '</select>' +
                '<span class="vas-qah-select-arr">' + icon("chevD") + '</span>' +
                '</div>' +
                '</div>' +
                '<button type="button" class="vas-qah-kebab" aria-hidden="true" tabindex="-1">' + icon("kebab") + '</button>' +
                '</div>';

            var right =
                disabledField("package", lbl("VAS_086_Product", "Product"), record.productName || record.itemName) +
                '<div class="vas-qah-qc-field active">' +
                '<div class="vas-qah-qc-ico">' + icon("calendar") + '</div>' +
                '<div class="vas-qah-qc-main">' +
                '<div class="vas-qah-qc-label">' + escapeHtml(lbl("VAS_086_QAQCDate", "QA/QC Date")) + '</div>' +
                '<input class="vas-qah-date" type="date" value="' + escapeHtml(qaDate) + '"/>' +
                '</div>' +
                '</div>' +
                '<div class="vas-qah-qc-field active note">' +
                '<div class="vas-qah-qc-main">' +
                '<div class="vas-qah-qc-label">' + escapeHtml(lbl("VAS_086_Description", "Description")) + '</div>' +
                '<textarea class="vas-qah-desc" rows="2" placeholder="' + escapeHtml(lbl("VAS_086_AddNoteOptional", "Add a note (optional)")) + '">' + escapeHtml(record.description || "") + '</textarea>' +
                '</div>' +
                '</div>';

            $dialogBody.html(
                '<div class="vas-qah-qc-headline">' + escapeHtml(lbl("VAS_086_QualityControl", "Quality Control")) + '</div>' +
                '<div class="vas-qah-qc-grid">' +
                '<div class="vas-qah-qc-col">' + left + '</div>' +
                '<div class="vas-qah-qc-col">' + right + '</div>' +
                '</div>' +
                '<div class="vas-qah-action">' +
                '<button type="button" class="vas-qah-save"' + saveDisabled + saveTitle + '>' + icon("check") + escapeHtml(lbl("VAS_086_SaveQAResult", "Save QA Result")) + '</button>' +
                '</div>'
            );

            updateActualTone();
        }

        function updateActualTone() {
            var $field = $dialogBody.find('.vas-qah-actual').closest('.vas-qah-qc-field');
            var value = $dialogBody.find('.vas-qah-actual').val();
            $field.removeClass('res-ok res-bad');
            if (value === "Working Fine") { $field.addClass('res-ok'); }
            if (value === "Not Satisfactory") { $field.addClass('res-bad'); }
        }

        function saveQAResult() {
            if (!activeRecord || activeRecord.qaRecordId <= 0) { return; }

            var actualValue = $dialogBody.find('.vas-qah-actual').val();
            if (!actualValue) {
                VIS.ADialog.info("", true, lbl("VAS_086_SelectQAActualValue", "Select an actual value before saving."), null);
                return;
            }

            showDialogBusy(true);
            $dialogBody.find('.vas-qah-save').prop('disabled', true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_086_QAHoldsWidget/SaveQAResult',
                type: 'POST',
                cache: false,
                data: {
                    qaRecordId: activeRecord.qaRecordId,
                    actualValue: actualValue,
                    qaQcDate: $dialogBody.find('.vas-qah-date').val(),
                    description: $dialogBody.find('.vas-qah-desc').val()
                },
                success: function (res) {
                    var data = parseResponse(res);
                    if (!data || data.error) {
                        VIS.ADialog.info("", true, data && data.error ? data.error : lbl("VAS_086_SaveFailed", "Save failed."), null);
                        return;
                    }

                    closeDetail();
                    loadPage(pageNo);
                    $(document).trigger('vas-qaholds-updated');
                },
                error: function () {
                    VIS.ADialog.info("", true, lbl("VAS_086_SaveFailed", "Save failed."), null);
                },
                complete: function () {
                    showDialogBusy(false);
                    if ($dialogBody) { $dialogBody.find('.vas-qah-save').prop('disabled', false); }
                }
            });
        }

        function closeDetail() {
            if (!$dialog) { return; }
            activeRecord = null;
            $dialog.addClass('vas-qah-hidden');
            $('body').removeClass('vas-qah-body-lock');
        }

        this.refreshWidget = function () {
            pageNo = 1;
            loadPage(1);
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $(document).off('keydown.vas-qah');
            $('body').removeClass('vas-qah-body-lock');
            if (rowResizeObserver) { rowResizeObserver.disconnect(); rowResizeObserver = null; }
            if ($dialog) { $dialog.remove(); $dialog = null; }
            $root.remove();
        };
    };

    VAS.VAS_086_QAHoldsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_086_QAHoldsWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_086_QAHoldsWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_086_QAHoldsWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
