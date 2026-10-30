/**
 * Pending Transfer Confirmation Widget (Material Transfer Dashboard)
 * Purpose - 3x2 operational work-queue list widget with a two-step confirmation modal.
 *           Lists pending transfer documents awaiting destination confirmation.
 * Prefix  - VAS_172_
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    // Material Transfer window (new name first, legacy name as the zoom fallback).
    var TRANSFER_ZOOM_WINDOW_NEW = 'VAS_MaterialTransfer';
    var TRANSFER_ZOOM_WINDOW_OLD = 'Material Transfer';

    // Longest Description the line takes (the controller truncates to the same).
    var DESCRIPTION_MAX = 255;

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

    // A message the framework does not know comes back empty, as the key, or as "[key]" - in
    // any of those cases the English fallback is what the user must see.
    function lbl(key, fallback) {
        var t = '';
        try { t = VIS.Msg.getMsg(key); } catch (e) { t = ''; }
        if (t && String(t).charAt(0) !== '[' && t !== key) { return t; }
        return (fallback !== undefined) ? fallback : key;
    }

    function escapeHtml(v) {
        return String(v == null ? '' : v)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;')
            .replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#039;');
    }

    // Quantities are shown without floating-point noise (0.1 + 0.2 must read 0.3).
    function fmtQty(n) {
        var v = Number(n);
        if (isNaN(v)) { return '0'; }
        return String(parseFloat(v.toFixed(6)));
    }

    function roundQty(n) { return parseFloat(Number(n).toFixed(6)); }

    function monthNames() {
        return lbl('VAS_172_Months', 'Jan,Feb,Mar,Apr,May,Jun,Jul,Aug,Sep,Oct,Nov,Dec').split(',');
    }

    function formatDate(iso) {
        if (!iso) { return '—'; }
        var d = new Date(iso);
        if (isNaN(d)) { return iso; }
        return d.getDate() + ' ' + monthNames()[d.getMonth()] + ' ' + d.getFullYear();
    }

    // ===== NEW CODE START — currency format (agent C06, 2026-08-19) =====
    // Organization-aware currency formatter (Note: VAS_172 carries no monetary values per Material Transfer spec; provided for standard helper completeness)
    function formatCurrency(val, iso, symbol) {
        if (val === null || val === undefined || isNaN(val)) return '—';
        var num = Number(val);
        var isIndian = (iso === 'INR');
        var formatted = '';
        if (isIndian) {
            if (Math.abs(num) >= 10000000) {
                formatted = (num / 10000000).toFixed(2) + ' Cr';
            } else if (Math.abs(num) >= 100000) {
                formatted = (num / 100000).toFixed(2) + ' Lakh';
            } else {
                formatted = num.toLocaleString('en-IN', { maximumFractionDigits: 2 });
            }
        } else {
            if (Math.abs(num) >= 1000000000) {
                formatted = (num / 1000000000).toFixed(2) + 'B';
            } else if (Math.abs(num) >= 1000000) {
                formatted = (num / 1000000).toFixed(2) + 'M';
            } else {
                formatted = num.toLocaleString(undefined, { maximumFractionDigits: 2 });
            }
        }
        return (symbol ? symbol + ' ' : '') + formatted;
    }
    // ===== NEW CODE END — currency format =====
    // ----- OLD CODE (kept for rollback, do not delete) -----
    // ----- END OLD CODE -----

    function deriveLineStatus(confirmed, target) {
        if (confirmed === null || confirmed === undefined) {
            return { status: 'Pending', cls: 'vas-172-pill--amber', text: lbl('VAS_172_Pending', 'Pending') };
        }
        var c = Number(confirmed);
        var t = Number(target);
        if (c === t) {
            return { status: 'Matched', cls: 'vas-172-pill--ok', text: lbl('VAS_172_Matched', 'Matched') };
        } else if (c < t) {
            return { status: 'Short', cls: 'vas-172-pill--bad', text: lbl('VAS_172_Short', 'Short') + ' (' + fmtQty(t - c) + ')' };
        } else {
            return { status: 'Over', cls: 'vas-172-pill--amber', text: lbl('VAS_172_Over', 'Over') + ' (+' + fmtQty(c - t) + ')' };
        }
    }

    // Quantity precision: the UOM's StdPrecision decides how many decimals may be typed
    // (0 = whole numbers only). Anything else typed is stripped.
    function sanitizeQty(raw, prec) {
        var s = String(raw == null ? '' : raw).replace(/[^0-9.]/g, '');
        var dot = s.indexOf('.');
        if (prec <= 0) { return dot >= 0 ? s.substring(0, dot) : s; }
        if (dot >= 0) {
            s = s.substring(0, dot + 1) + s.substring(dot + 1).replace(/\./g, '').substring(0, prec);
        }
        return s;
    }

    function numOf(s) {
        var v = parseFloat(s);
        return isNaN(v) ? 0 : v;
    }

    var infoTimer = null;

    function showInfo(key, fallback) {
        var msg = lbl(key, fallback);
        // While the Review Confirmation Line pop-up is open the message is shown inside it: the
        // framework dialog can end up under the pop-up's fixed overlay, so a quantity was reset
        // with no visible reason (30-Oct test pass).
        var box = document.getElementById('vas172-msg');
        if (box) {
            box.textContent = msg;
            box.hidden = false;
            clearTimeout(infoTimer);
            infoTimer = setTimeout(function () { box.hidden = true; }, 6000);
            return;
        }
        // Already-resolved text goes in the raw-text argument with an empty key (as VAS_090 does).
        try { VIS.ADialog.info("", true, msg); }
        catch (e) { alert(msg); }
    }

    VAS.VAS_172_PendingTransferConfirmationWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $wrapper = $('<div class="vas-172-container">');
        var $root = $('<div class="vas-172-root vas-172-card">');

        var allQueueRecords = [];
        var PAGE_SIZE = 3;
        var currentPage = 1;

        var $modal = null;
        var activeTransfer = null;
        var activeLine = null;

        function buildWidget() {
            $root.html(
                '<div class="vas-172-header-row">' +
                    '<div class="vas-172-left-cluster">' +
                        '<div class="vas-172-icon-well">' +
                            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                                '<polyline points="20 6 9 17 4 12"></polyline>' +
                            '</svg>' +
                        '</div>' +
                        '<div class="vas-172-title-block">' +
                            '<div class="vas-172-title">' + escapeHtml(lbl('VAS_172_PendingTransferConfirmation', 'Pending Transfer Confirmation')) + '</div>' +
                            '<div class="vas-172-subtitle">' + escapeHtml(lbl('VAS_172_RequiringConfirmation', 'Requiring confirmation')) + '</div>' +
                        '</div>' +
                    '</div>' +
                '</div>' +
                '<div class="vas-172-body"></div>' +
                '<div class="vas-172-footer"></div>'
            );

            $wrapper.append($root);
        }

        function renderQueue() {
            var $body = $root.find('.vas-172-body');
            var $footer = $root.find('.vas-172-footer');
            var total = allQueueRecords.length;
            var totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));
            currentPage = Math.min(currentPage, totalPages);

            if (total === 0) {
                $body.html('<div class="vas-172-empty">' + escapeHtml(lbl('VAS_172_NoPendingConfirmations', 'No pending confirmations')) + '</div>');
                renderFooter($footer, 0, 0, 0, 1, 1);
                return;
            }

            var start = (currentPage - 1) * PAGE_SIZE;
            var end = Math.min(start + PAGE_SIZE, total);
            var slice = allQueueRecords.slice(start, end);

            var html = '<div class="vas-172-queue">';
            for (var i = 0; i < slice.length; i++) {
                var item = slice[i];
                var routeStr = (item.SourceLocator || item.SourceWarehouse || 'Src') + ' → ' + (item.DestLocator || item.DestWarehouse || 'Dst') + ' · ' + (item.LineCount || 0) + ' ' + (item.LineCount === 1 ? lbl('VAS_172_LineCountSingular', 'line') : lbl('VAS_172_LineCountPlural', 'lines'));
                var statusPill = '<span class="vas-172-pill vas-172-pill--amber">' + escapeHtml(lbl('VAS_172_Pending', 'Pending')) + '</span>';

                html += '<button type="button" class="vas-172-row' + (i === slice.length - 1 ? ' vas-172-row--last' : '') + '" data-idx="' + (start + i) + '">' +
                    '<div class="vas-172-row-top">' +
                        '<span class="vas-172-trno">' + escapeHtml(item.TransferNo || '—') + '</span>' +
                        statusPill +
                    '</div>' +
                    '<div class="vas-172-row-meta">' + escapeHtml(routeStr) + '</div>' +
                '</button>';
            }
            html += '</div>';
            $body.html(html);

            $body.find('.vas-172-row').on('click', function () {
                var idx = parseInt($(this).attr('data-idx'), 10);
                openModal(allQueueRecords[idx]);
            });

            renderFooter($footer, start + 1, end, total, currentPage, totalPages);
        }

        function renderFooter($footer, s, e, total, page, totalPages) {
            var showingTxt = total === 0 ? '' :
                lbl('VAS_172_Showing', 'Showing') + ' ' + s + '–' + e + ' ' + lbl('VAS_172_Of', 'of') + ' ' + total;
            $footer.html(
                '<span class="vas-172-footer-helper">' + escapeHtml(showingTxt) + '</span>' +
                '<span class="vas-172-pager">' +
                    '<button type="button" class="vas-172-pager-btn" id="vas172-prev"' + (page <= 1 ? ' disabled' : '') + '>&#8249;</button>' +
                    '<span class="vas-172-pager-label">' + page + ' ' + lbl('VAS_172_Of', 'of') + ' ' + totalPages + '</span>' +
                    '<button type="button" class="vas-172-pager-btn" id="vas172-next"' + (page >= totalPages ? ' disabled' : '') + '>&#8250;</button>' +
                '</span>'
            );
            $footer.find('#vas172-prev').on('click', function () { if (currentPage > 1) { currentPage--; renderQueue(); } });
            $footer.find('#vas172-next').on('click', function () { if (currentPage < totalPages) { currentPage++; renderQueue(); } });
        }

        function loadData() {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_172_PendingTransferConfirmationWidget/GetPendingConfirmations',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'string') { data = JSON.parse(data); }
                    allQueueRecords = (data && !data.error && Array.isArray(data.records)) ? data.records : [];
                    currentPage = 1;
                    renderQueue();
                },
                error: function () {
                    allQueueRecords = [];
                    renderQueue();
                }
            });
        }

        function openModal(tr) {
            activeTransfer = tr;
            renderModalView1();
        }

        function closeModal() {
            if ($modal) { $modal.remove(); $modal = null; }
            activeTransfer = null;
            activeLine = null;
        }

        // Opens the transfer. The pop-up is closed first so nothing stays behind its scrim.
        // Hosted on the Material Transfer window (windowNo >= 0) the window's own grid is navigated
        // to the record (same pattern as InvoicesWidget / VAS_227); on a home dashboard the
        // Material Transfer window is opened on it.
        function openRecord() {
            var tr = activeTransfer;
            if (!tr || !tr.TransferID) { return; }
            var id = Number(tr.TransferID);
            closeModal();
            try {
                if ($self.windowNo >= 0) {
                    $self.widgetFirevalueChanged({
                        "TabWhereClause": "M_Movement.M_Movement_ID=" + id,
                        "TabLayout": "Y",
                        "TabIndex": "0"
                    });
                } else {
                    VAS.ZoomUtil.zoomToRecord('M_Movement_ID', id, 0, TRANSFER_ZOOM_WINDOW_NEW, TRANSFER_ZOOM_WINDOW_OLD);
                }
            } catch (e) { }
        }

        function openRecordButton() {
            return '<button type="button" class="vas-172-btn-open" id="vas172-btn-open">' + escapeHtml(lbl('VAS_172_OpenRecord', 'Open Record')) + '</button>';
        }

        function renderModalView1() {
            var tr = activeTransfer;
            var lines = tr.Lines || [];
            var allMatched = (lines.length > 0);

            var linesHtml = '';
            for (var i = 0; i < lines.length; i++) {
                var l = lines[i];
                var s = deriveLineStatus(l.ConfirmedQty, l.TargetQty);
                if (s.status !== 'Matched') { allMatched = false; }

                var confStr = (l.ConfirmedQty === null || l.ConfirmedQty === undefined) ? '–' : fmtQty(l.ConfirmedQty);
                linesHtml +=
                    '<button type="button" class="vas-172-line-row" data-lidx="' + i + '">' +
                        '<div class="vas-172-line-left">' +
                            '<div class="vas-172-line-no">' + escapeHtml(lbl('VAS_172_LineNoPrefix', 'Line') + ' ' + (l.LineNo || (i + 1))) + '</div>' +
                            '<div class="vas-172-line-sub">' + escapeHtml((l.ItemName || lbl('VAS_172_Item', 'Item')) + ' · ' + (l.UOM || '')) + '</div>' +
                        '</div>' +
                        '<div class="vas-172-line-count">' + confStr + ' / ' + fmtQty(l.TargetQty) + '</div>' +
                        '<span class="vas-172-pill ' + s.cls + '">' + escapeHtml(s.text) + '</span>' +
                        '<span class="vas-172-chevron">&#8250;</span>' +
                    '</button>';
            }

            var modalHtml =
                '<div class="vas-172-modal-scrim">' +
                    '<div class="vas-172-modal">' +
                        '<div class="vas-172-modal-header">' +
                            '<span class="vas-172-modal-title">' + escapeHtml((tr.TransferNo || 'TR') + ' · ' + lbl('VAS_172_TransferConfirmation', 'Transfer Confirmation')) + '</span>' +
                            '<span class="vas-172-pill vas-172-pill--amber">' + escapeHtml(lbl('VAS_172_Pending', 'Pending')) + '</span>' +
                            openRecordButton() +
                            '<button type="button" class="vas-172-modal-close" id="vas172-m-close">&#215;</button>' +
                        '</div>' +
                        '<div class="vas-172-modal-body">' +
                            '<div class="vas-172-field-grid">' +
                                '<div class="vas-172-field">' +
                                    '<div class="vas-172-field-label">' + escapeHtml(lbl('VAS_172_DocumentNumber', 'Document Number')) + '</div>' +
                                    '<div class="vas-172-field-value">' + escapeHtml(tr.TransferNo || '—') + '</div>' +
                                '</div>' +
                                '<div class="vas-172-field">' +
                                    '<div class="vas-172-field-label">' + escapeHtml(lbl('VAS_172_Route', 'Route')) + '</div>' +
                                    '<div class="vas-172-field-value">' + escapeHtml((tr.SourceLocator || '') + ' → ' + (tr.DestLocator || '')) + '</div>' +
                                '</div>' +
                                '<div class="vas-172-field">' +
                                    '<div class="vas-172-field-label">' + escapeHtml(lbl('VAS_172_FromToWarehouse', 'From → To Warehouse')) + '</div>' +
                                    '<div class="vas-172-field-value">' + escapeHtml((tr.SourceWarehouse || '') + ' → ' + (tr.DestWarehouse || '')) + '</div>' +
                                '</div>' +
                                '<div class="vas-172-field">' +
                                    '<div class="vas-172-field-label">' + escapeHtml(lbl('VAS_172_Date', 'Date')) + '</div>' +
                                    '<div class="vas-172-field-value">' + escapeHtml(formatDate(tr.MovementDate)) + '</div>' +
                                '</div>' +
                            '</div>' +
                            '<div class="vas-172-section-heading">' + escapeHtml(lbl('VAS_172_ConfirmationLines', 'Confirmation Lines')) + '</div>' +
                            '<div class="vas-172-lines-list-container">' +
                                '<div class="vas-172-lines-list">' + linesHtml + '</div>' +
                            '</div>' +
                            '<div class="vas-172-action-bar">' +
                                '<button type="button" class="vas-172-btn-complete' + (!allMatched ? ' vas-172-btn-complete--disabled' : '') + '" id="vas172-btn-comp"' + (!allMatched ? ' disabled' : '') + '>' + escapeHtml(lbl('VAS_172_CompleteTransferConfirmation', 'Complete Transfer Confirmation')) + '</button>' +
                            '</div>' +
                        '</div>' +
                    '</div>' +
                '</div>';

            if ($modal) { $modal.remove(); }
            $modal = $(modalHtml);
            $('body').append($modal);

            $modal.find('#vas172-m-close').on('click', closeModal);
            $modal.find('#vas172-btn-open').on('click', openRecord);

            $modal.find('.vas-172-line-row').on('click', function () {
                var lidx = parseInt($(this).attr('data-lidx'), 10);
                activeLine = lines[lidx];
                renderModalView2();
            });

            $modal.find('#vas172-btn-comp').on('click', function () {
                if (!allMatched) { return; }
                completeConfirmation();
            });
        }

        function renderModalView2() {
            var tr = activeTransfer;
            var l = activeLine;

            var target = Number(l.TargetQty || 0);
            var scrap0 = Number(l.ScrappedQty || 0);
            // A line not confirmed yet (no value, or the untouched 0) starts at its target.
            // So the default is Confirmed = Target, Difference = 0 (Target 10 -> Confirmed 10).
            // A stored figure that breaks Target = Confirmed + Difference + Scrapped (confirmed above
            // target, or confirmed + scrapped above target) is not trusted either.
            if (scrap0 > target) { scrap0 = 0; }
            var unconfirmed = (l.ConfirmedQty === null || l.ConfirmedQty === undefined) ||
                (Number(l.ConfirmedQty) === 0 && scrap0 === 0) ||
                roundQty(Number(l.ConfirmedQty) + scrap0) > target;
            var prefillConf = unconfirmed ? roundQty(target - scrap0) : Number(l.ConfirmedQty);
            var initialDiff = roundQty(target - prefillConf - scrap0);
            var diffCls = initialDiff === 0 ? 'vas-172-diff--ok' : 'vas-172-diff--bad';

            var prec = (l.UOMPrecision === null || l.UOMPrecision === undefined || isNaN(Number(l.UOMPrecision))) ? 0 : Number(l.UOMPrecision);
            var inputMode = prec > 0 ? 'decimal' : 'numeric';

            var modalHtml =
                '<div class="vas-172-modal-scrim">' +
                    '<div class="vas-172-modal">' +
                        '<div class="vas-172-modal-header">' +
                            '<span class="vas-172-modal-title">' + escapeHtml(lbl('VAS_172_ReviewConfirmationLine', 'Review Confirmation Line')) + '</span>' +
                            openRecordButton() +
                            '<button type="button" class="vas-172-modal-close" id="vas172-m-close2">&#215;</button>' +
                        '</div>' +
                        '<div class="vas-172-modal-body vas-172-modal-body--v2">' +
                            '<button type="button" class="vas-172-back-link" id="vas172-b2">&#8249; ' + escapeHtml(lbl('VAS_172_BackTo', 'Back to')) + ' ' + escapeHtml(tr.TransferNo || 'TR') + '</button>' +
                            '<div class="vas-172-v2-grid">' +
                                '<div class="vas-172-v2-field vas-172-v2-readonly">' +
                                    '<div class="vas-172-field-label">' + escapeHtml(lbl('VAS_172_TransferLine', 'Transfer Line')) + '</div>' +
                                    '<div class="vas-172-field-value">' + escapeHtml(lbl('VAS_172_LineNoPrefix', 'Line') + ' ' + (l.LineNo || 1) + ' · ' + (l.ItemName || lbl('VAS_172_Item', 'Item'))) + '</div>' +
                                '</div>' +
                                '<div class="vas-172-v2-field vas-172-v2-readonly">' +
                                    '<div class="vas-172-field-label">' + escapeHtml(lbl('VAS_172_UOM', 'UOM')) + '</div>' +
                                    '<div class="vas-172-field-value">' + escapeHtml(l.UOM || '—') + '</div>' +
                                '</div>' +
                                '<div class="vas-172-v2-field vas-172-v2-readonly">' +
                                    '<div class="vas-172-field-label">' + escapeHtml(lbl('VAS_172_AttributeSetInstance', 'Attribute Set Instance')) + '</div>' +
                                    '<div class="vas-172-field-value">' + escapeHtml(l.AttributeSetInstance || '—') + '</div>' +
                                '</div>' +
                                '<div class="vas-172-v2-field vas-172-v2-editable">' +
                                    '<div class="vas-172-field-label">' + escapeHtml(lbl('VAS_172_ReceivingLocator', 'Receiving Locator')) + '</div>' +
                                    '<select class="vas-172-select" id="vas172-sel-recv"></select>' +
                                '</div>' +
                                '<div class="vas-172-v2-field vas-172-v2-readonly">' +
                                    '<div class="vas-172-field-label">' + escapeHtml(lbl('VAS_172_TargetQuantity', 'Target Quantity')) + '</div>' +
                                    '<div class="vas-172-field-value" style="font-weight:700;">' + fmtQty(target) + '</div>' +
                                '</div>' +
                                '<div class="vas-172-v2-field vas-172-v2-readonly">' +
                                    '<div class="vas-172-field-label">' + escapeHtml(lbl('VAS_172_Difference', 'Difference')) + '</div>' +
                                    '<div class="vas-172-field-value ' + diffCls + '" id="vas172-diff-val">' + fmtQty(initialDiff) + '</div>' +
                                '</div>' +
                                '<div class="vas-172-v2-field vas-172-v2-editable">' +
                                    '<div class="vas-172-field-label">' + escapeHtml(lbl('VAS_172_ConfirmedQuantity', 'Confirmed Quantity')) + '</div>' +
                                    '<input type="text" inputmode="' + inputMode + '" autocomplete="off" class="vas-172-input" id="vas172-inp-conf" value="' + fmtQty(prefillConf) + '">' +
                                '</div>' +
                                '<div class="vas-172-v2-field vas-172-v2-editable">' +
                                    '<div class="vas-172-field-label">' + escapeHtml(lbl('VAS_172_ScrappedQuantity', 'Scrapped Quantity')) + '</div>' +
                                    '<input type="text" inputmode="' + inputMode + '" autocomplete="off" class="vas-172-input" id="vas172-inp-scrap" value="' + fmtQty(scrap0) + '">' +
                                '</div>' +
                                '<div class="vas-172-v2-field vas-172-v2-editable vas-172-v2-span2">' +
                                    '<div class="vas-172-field-label">' + escapeHtml(lbl('VAS_172_ScrapLocator', 'Scrap Locator')) + '</div>' +
                                    '<select class="vas-172-select" id="vas172-sel-scrap"></select>' +
                                '</div>' +
                                '<div class="vas-172-v2-field vas-172-v2-editable vas-172-v2-span2">' +
                                    '<div class="vas-172-field-label">' + escapeHtml(lbl('VAS_172_DescriptionNote', 'Description / Note')) + '</div>' +
                                    '<textarea class="vas-172-textarea" id="vas172-inp-desc" rows="2" maxlength="' + DESCRIPTION_MAX + '" placeholder="' + escapeHtml(lbl('VAS_172_AddNoteOptional', 'Add a note (optional)')) + '">' + escapeHtml(l.Description || '') + '</textarea>' +
                                '</div>' +
                            '</div>' +
                            '<div class="vas-172-v2-msg" id="vas172-msg" role="alert" hidden></div>' +
                            '<div class="vas-172-v2-footer">' +
                                '<button type="button" class="vas-172-btn-save" id="vas172-btn-save">' + escapeHtml(lbl('VAS_172_SaveLine', 'Save Line')) + '</button>' +
                            '</div>' +
                        '</div>' +
                    '</div>' +
                '</div>';

            if ($modal) { $modal.remove(); }
            $modal = $(modalHtml);
            $('body').append($modal);

            $modal.find('#vas172-m-close2').on('click', closeModal);
            $modal.find('#vas172-btn-open').on('click', openRecord);
            $modal.find('#vas172-b2').on('click', function () { renderModalView1(); });

            // ---- Receiving / scrap locator: every locator of the To Warehouse, by locator NAME (not the
            //      search key / code); the line's current locator is always one of them ----
            var $sel = $modal.find('#vas172-sel-scrap');
            var $recv = $modal.find('#vas172-sel-recv');
            var scrapLocatorId = Number(l.ScrapLocatorID || 0);
            var recvLocatorId0 = Number(l.ReceivingLocatorID || 0);
            var recvLocatorId = recvLocatorId0;
            var fillSelect = function ($s, locs, curId, curName) {
                var html = '';
                var found = false;
                for (var i = 0; i < locs.length; i++) {
                    if (Number(locs[i].LocatorID) === curId) { found = true; }
                    html += '<option value="' + locs[i].LocatorID + '">' + escapeHtml(locs[i].LocatorName || '') + '</option>';
                }
                if (!found && curId > 0) {
                    html = '<option value="' + curId + '">' + escapeHtml(curName || '') + '</option>' + html;
                }
                if (html === '') { html = '<option value="0">—</option>'; }
                $s.html(html);
                $s.val(String(curId > 0 ? curId : $s.find('option').first().val()));
            };
            var fillLocators = function (locs) {
                fillSelect($sel, locs, scrapLocatorId, l.ScrapLocator);
                fillSelect($recv, locs, recvLocatorId, l.ReceivingLocator);
            };
            fillLocators([]);
            if (l.LineID) {
                $.ajax({
                    url: VIS.Application.contextUrl + 'VAS_172_PendingTransferConfirmationWidget/GetLocators',
                    type: 'GET',
                    cache: false,
                    data: { lineId: l.LineID },
                    success: function (res) {
                        var data = typeof res === 'string' ? JSON.parse(res) : res;
                        if (data && typeof data === 'string') { data = JSON.parse(data); }
                        if (data && !data.error && Array.isArray(data.locators) && $modal && $sel.closest('body').length) {
                            // Keep whatever the user already picked while the list was loading.
                            scrapLocatorId = Number($sel.val() || scrapLocatorId);
                            recvLocatorId = Number($recv.val() || recvLocatorId);
                            fillLocators(data.locators);
                        }
                    }
                });
            }

            // ---- Quantities: Target = Confirmed + Difference + Scrapped, always ----
            var $conf = $modal.find('#vas172-inp-conf');
            var $scrap = $modal.find('#vas172-inp-scrap');
            var lastConf = $conf.val();
            var lastScrap = $scrap.val();

            var showDiff = function () {
                var diff = roundQty(target - numOf($conf.val()) - numOf($scrap.val()));
                var $d = $modal.find('#vas172-diff-val');
                $d.text(fmtQty(diff));
                $d.attr('class', 'vas-172-field-value ' + (diff === 0 ? 'vas-172-diff--ok' : 'vas-172-diff--bad'));
            };

            var onQtyInput = function (isConf) {
                var $inp = isConf ? $conf : $scrap;
                var clean = sanitizeQty($inp.val(), prec);
                if (clean !== $inp.val()) { $inp.val(clean); }

                var conf = numOf($conf.val());
                var scrap = numOf($scrap.val());
                var msgKey = null, msgFallback = '';
                if (isConf && conf > target) {
                    msgKey = 'VAS_172_ConfQtyExceedsTarget'; msgFallback = 'Confirmed Quantity cannot be greater than Target Quantity.';
                } else if (!isConf && scrap > target) {
                    msgKey = 'VAS_172_ScrapQtyExceedsTarget'; msgFallback = 'Scrapped Quantity cannot be greater than Target Quantity.';
                } else if (roundQty(conf + scrap) > target) {
                    msgKey = 'VAS_172_ConfScrapExceedsTarget'; msgFallback = 'Confirmed Quantity plus Scrapped Quantity cannot be greater than Target Quantity.';
                }

                if (msgKey) {
                    // Revert to the last accepted value, then tell the user why.
                    $inp.val(isConf ? lastConf : lastScrap);
                    showDiff();
                    showInfo(msgKey, msgFallback);
                    return;
                }
                if (isConf) { lastConf = $conf.val(); } else { lastScrap = $scrap.val(); }
                showDiff();
            };

            $conf.on('input', function () { onQtyInput(true); });
            $scrap.on('input', function () { onQtyInput(false); });

            $modal.find('#vas172-btn-save').on('click', function () {
                var confVal = parseFloat($conf.val());
                var scrapVal = parseFloat($scrap.val());
                var descVal = String($modal.find('#vas172-inp-desc').val() || '');
                if (descVal.length > DESCRIPTION_MAX) { descVal = descVal.substring(0, DESCRIPTION_MAX); }

                if (isNaN(confVal) || confVal < 0) {
                    alert(lbl('VAS_172_ConfirmedQtyValidation', 'Confirmed quantity must be a number of zero or more.'));
                    return;
                }
                if (isNaN(scrapVal) || scrapVal < 0) { scrapVal = 0; }
                if (roundQty(confVal + scrapVal) > target) {
                    showInfo('VAS_172_ConfScrapExceedsTarget', 'Confirmed Quantity plus Scrapped Quantity cannot be greater than Target Quantity.');
                    return;
                }

                var selId = Number($sel.val() || 0);
                var selName = $sel.find('option:selected').text();
                var recvId = Number($recv.val() || 0);
                var recvName = $recv.find('option:selected').text();
                // Only a changed receiving locator is sent; it then wins over the scrap locator
                // (both land on the line's destination locator).
                var recvChanged = recvId > 0 && recvId !== recvLocatorId0;

                l.ConfirmedQty = confVal;
                l.ScrappedQty = scrapVal;
                l.Description = descVal;
                if (recvChanged) {
                    l.ReceivingLocatorID = recvId; l.ReceivingLocator = recvName;
                    l.ScrapLocatorID = recvId; l.ScrapLocator = recvName;
                } else if (selId > 0) { l.ScrapLocatorID = selId; l.ScrapLocator = selName; }

                $.ajax({
                    url: VIS.Application.contextUrl + 'VAS_172_PendingTransferConfirmationWidget/SaveConfirmationLine',
                    type: 'POST',
                    contentType: 'application/json',
                    data: JSON.stringify({
                        LineID: l.LineID || 0,
                        TransferID: tr.TransferID || 0,
                        LineNo: l.LineNo || 1,
                        ConfirmedQty: confVal,
                        ScrappedQty: scrapVal,
                        ScrapLocatorID: selId,
                        ReceivingLocatorID: recvChanged ? recvId : 0,
                        Description: descVal
                    }),
                    success: function () {
                        renderModalView1();
                    },
                    error: function () {
                        renderModalView1();
                    }
                });
            });
        }

        function completeConfirmation() {
            var tr = activeTransfer;
            if (!tr) { return; }

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_172_PendingTransferConfirmationWidget/CompleteTransfer',
                type: 'POST',
                contentType: 'application/json',
                data: JSON.stringify({
                    TransferID: tr.TransferID || 0
                }),
                success: function () {
                    closeModal();
                    loadData();
                },
                error: function () {
                    closeModal();
                    loadData();
                }
            });
        }

        this.Initalize = function () {
            buildWidget();
            loadData();
        };

        this.refreshWidget = function () { loadData(); };

        this.getRoot = function () { return $wrapper; };

        this.disposeComponent = function () {
            closeModal();
            $root.off();
            $root.remove();
            $wrapper.remove();
        };
    };

    VAS.VAS_172_PendingTransferConfirmationWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_172_PendingTransferConfirmationWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_172_PendingTransferConfirmationWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_172_PendingTransferConfirmationWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_172_PendingTransferConfirmationWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_172_PendingTransferConfirmationWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
