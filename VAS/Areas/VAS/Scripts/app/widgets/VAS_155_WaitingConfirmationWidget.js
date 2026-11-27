/**
 * Waiting Confirmation Widget (Delivery Order dashboard)
 * Widget number 155.
 * Widget size: 3 columns x 2 rows.
 * Worklist of drafted/in-progress CUSTOMER delivery confirmations
 * (M_InOutConfirm joined to M_InOut with IsSOTrx='Y' AND MovementType='C-',
 * so vendor/receiving confirmations never appear here). A row click opens
 * that confirmation directly (no intermediate list modal): header fields,
 * 5 lines per page, a line click opens the Review Confirmation Line state
 * inside the same modal shell (Scrap Locator / Confirmed Qty / Scrapped
 * Qty / Description editable; DO Line / Product / UOM / Attribute Set
 * Instance / Target Qty / Difference read-only). Footer actions: Mark In
 * Dispute (IsInDispute only, never DocStatus) and Complete Confirmation
 * (standard document engine, DOCACTION_Complete - disabled until every
 * line is fully accounted for). Read-only from the widget's own list view;
 * writes only happen through the three explicit actions above. Plain DOM
 * would work but this widget follows the sibling Delivery Order widgets'
 * jQuery convention for the framework boundary.
 * Backend - VAS_155_WaitingConfirmationWidget/GetWaitingConfirmations
 *           VAS_155_WaitingConfirmationWidget/GetConfirmationDetail
 *           VAS_155_WaitingConfirmationWidget/GetWarehouseLocators
 *           VAS_155_WaitingConfirmationWidget/SaveConfirmationLine
 *           VAS_155_WaitingConfirmationWidget/MarkInDispute
 *           VAS_155_WaitingConfirmationWidget/CompleteConfirmation
 * Summary Message Table
 *  # | Current Text                                     | Message Key
 * ---+--------------------------------------------------+------------------------
 *  1 | Waiting Confirmation                             | VAS_155_WC_Title
 *  2 | Drafted and in-progress delivery confirmations   | VAS_155_WC_Subtitle
 *  3 | 3 Col x 2 Row                                    | VAS_155_WC_SizeBadge
 *  4 | Confirmation / Customer / Lines / Status         | VAS_155_WC_ColConfirmation / VAS_155_WC_ColCustomer / VAS_155_WC_ColLines / VAS_155_WC_ColStatus
 *  5 | Drafted / In Progress / In Dispute               | VAS_155_WC_Drafted / VAS_155_WC_InProgress / VAS_155_WC_InDispute
 *  6 | No drafted or in-progress confirmations          | VAS_155_WC_EmptyState
 *  7 | Loading confirmations...                         | VAS_155_WC_Loading
 *  8 | Unable to load confirmations / Retry             | VAS_155_WC_LoadError / VAS_155_WC_Retry
 *  9 | Showing / of                                     | VAS_Showing / VAS_Of
 * 10 | Previous page / Next page                        | VAS_PreviousPage / VAS_NextPage
 * 11 | Close                                             | Close
 * 12 | Source DO / Customer / Warehouse / Date          | VAS_155_WC_FieldSourceDO / VAS_155_WC_FieldCustomer / VAS_155_WC_FieldWarehouse / VAS_155_WC_FieldDate
 * 13 | Confirmation Lines                               | VAS_155_WC_LinesSectionTitle
 * 14 | Line (line number prepended in code)             | VAS_155_WC_LinePrefix
 * 15 | Matched / Pending                                | VAS_155_WC_Matched / VAS_155_WC_Pending
 * 16 | Loading confirmation lines...                    | VAS_155_WC_LinesLoading
 * 17 | Mark In Dispute / Complete Confirmation           | VAS_155_WC_MarkInDispute / VAS_155_WC_CompleteConfirmation
 * 18 | Complete all confirmation lines before processing the confirmation. | VAS_155_WC_CompleteDisabledHint
 * 19 | Review Confirmation Line                         | VAS_155_WC_ReviewLineTitle
 * 20 | Back to (confirmation number appended in code)   | VAS_155_WC_BackTo
 * 21 | DO Line / UOM / Attribute Set Instance / Scrap Locator | VAS_155_WC_FieldDOLine / VAS_155_WC_FieldUom / VAS_155_WC_FieldAttrSetInstance / VAS_155_WC_FieldScrapLocator
 * 22 | Target Quantity / Confirmed Quantity / Scrapped Quantity / Difference / Description | VAS_155_WC_FieldTargetQty / VAS_155_WC_FieldConfirmedQty / VAS_155_WC_FieldScrappedQty / VAS_155_WC_FieldDifference / VAS_155_WC_FieldDescription
 * 23 | Select a locator                                 | VAS_155_WC_SelectLocator
 * 24 | Save Line                                        | VAS_155_WC_SaveLine
 * 25 | Saving...                                        | VAS_155_WC_Saving
 * 26 | Save failed. / Not found.                        | VAS_155_WC_SaveFailed / VAS_155_WC_NotFound
 * VAS163 2026-11-27 (Sr 54-62):
 * 27 | Confirmed / Scrapped Quantity cannot be greater than Target Quantity. | VAS_090_ConfirmedAboveTarget / VAS_090_ScrappedAboveTarget (reused)
 * 28 | Quantity should not be greater than Target Quantity | VAS_172_ConfScrapExceedsTarget (reused)
 * 29 | Quantity exceeds the decimal places allowed for this UOM: (precision appended) | VAS_155_WC_QtyPrecision (new)
 * 30 | Confirmed / Scrapped quantity must be zero or a positive number. | VAS_155_WC_InvalidConfirmedQty / VAS_155_WC_InvalidScrappedQty
 * 31 | Quality Control / Test Parameter / Acceptable Value / Actual Value | VAS_090_QualityControl / VAS_104_TestParameter / VAS_104_AcceptableValue / VAS_104_ActualValue (reused)
 * 32 | Select / Save                                    | VAS_Select / VAS_Save (reused)
 * 33 | No quality parameters for this line.             | VAS_155_WC_NoQualityParams (new)
 * 34 | Select an Actual Value for at least one quality parameter. | VAS_155_WC_QcSelectValue (new)
 * Backend (Sr 62) - VAS_155_WaitingConfirmationWidget/GetLineQualityParameters
 *                   VAS_155_WaitingConfirmationWidget/SaveLineQualityParameters
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

    VAS.VAS_155_WaitingConfirmationWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="MPC-wc-root">');
        var $card, $body, $footHelper, $pager, $pageText, $prevButton, $nextButton;
        var $modal, $modalTitleGroup, $modalBody, $modalFooter, $modalClose;
        var listRequest, detailRequest, locatorsRequest, qcRequest; // VAS163 2026-11-27: Sr 62 qcRequest
        var eventNamespace = 'MPCWaitingConfirmation';
        var modalEventNamespace = '.MPCWcModal';
        var lastFocusedEl = null;

        var ROWS_PER_PAGE = 4;
        var LINES_PER_PAGE = 5;

        var STATUS_META = {
            drafted: { msgKey: 'VAS_155_WC_Drafted', fallback: 'Drafted', cls: 'MPC-wc-pill-drafted' },
            inProgress: { msgKey: 'VAS_155_WC_InProgress', fallback: 'In Progress', cls: 'MPC-wc-pill-inprogress' },
            dispute: { msgKey: 'VAS_155_WC_InDispute', fallback: 'In Dispute', cls: 'MPC-wc-pill-dispute' }
        };

        var state = {
            items: [],
            page: 0,
            loaded: false
        };

        var modalState = {
            view: 'detail',        // 'detail' | 'line'
            confirmId: null,
            header: null,
            lines: [],
            linePage: 0,
            lineIdx: null,
            locators: [],
            locatorsLoadedFor: 0,
            saving: false,
            error: ''
        };

        function lbl(key, fallback) {
            // VAS163 2026-11-27: Sr 54-62 English fallback while a new key is not yet seeded in AD_Message.
            var text = VIS.Msg.getMsg(key);
            if (!text || text === key || String(text).charAt(0) === '[') { return fallback || key; }
            return text;
        }

        function el(tag, className, text) {
            var node = document.createElement(tag);
            if (className) { node.className = className; }
            if (text != null) { node.textContent = text; }
            return node;
        }

        // VAS163 2026-11-27: Sr 48 hover text (title) so clipped values can always be read.
        function tip(node, text) {
            if (node && text != null && String(text).trim()) { node.title = String(text); }
            return node;
        }

        // VAS163 2026-11-27: Sr 48 a label element that records its message key.
        function keyed(node, key) {
            if (node && key) { node.setAttribute('data-msgkey', key); }
            return node;
        }

        // VAS163 2026-11-27: Sr 51 open the Delivery Order record (copied from VAS_145 / VAS_288 openRecordInWindow).
        var ZOOM_WINDOW_NAME_NEW = 'VAS_DeliveryOrder';
        var ZOOM_WINDOW_NAME_OLD = 'Shipment (Customer)';
        var doWindowId = 0;
        function openRecordInWindow(doId) {
            var recordId = Number(doId || 0);
            if (recordId <= 0) { return; }
            closeModal();
            if ($self.windowNo >= 0) {
                $self.widgetFirevalueChanged({
                    "TabWhereClause": "M_InOut.M_InOut_ID=" + recordId,
                    "TabLayout": "Y",
                    "TabIndex": "0"
                });
                return;
            }
            if (!window.VAS || !VAS.ZoomUtil || typeof VAS.ZoomUtil.zoomToRecord !== 'function') { return; }
            VAS.ZoomUtil.zoomToRecord('M_InOut_ID', recordId, doWindowId, ZOOM_WINDOW_NAME_NEW, ZOOM_WINDOW_NAME_OLD)
                .done(function (id) { if (id > 0) { doWindowId = id; } });
        }

        // VAS163 2026-11-27: Sr 57 number of decimals typed in a culture-neutral number string.
        function decimalPlaces(value) {
            var s = String(value == null ? '' : value).trim();
            var i = s.indexOf('.');
            if (i < 0) { return 0; }
            return s.substring(i + 1).replace(/0+$/, '').length;
        }

        // VAS163 2026-11-27: Sr 54-57 quantity rules of the Review Confirmation Line form; '' when valid.
        function validateLineQty(line, confirmedText, scrappedText) {
            var target = Number(line.targetQty) || 0;
            var precision = Math.max(0, Number(line.uomPrecision) || 0);
            var c = parseFloat(confirmedText) || 0;
            var s = parseFloat(scrappedText) || 0;
            if (decimalPlaces(confirmedText) > precision || decimalPlaces(scrappedText) > precision) {
                return lbl('VAS_155_WC_QtyPrecision', 'Quantity exceeds the decimal places allowed for this UOM:') + ' ' + precision;
            }
            if (c < 0) { return lbl('VAS_155_WC_InvalidConfirmedQty', 'Confirmed quantity must be zero or a positive number.'); }
            if (s < 0) { return lbl('VAS_155_WC_InvalidScrappedQty', 'Scrapped quantity must be zero or a positive number.'); }
            if (c > target) { return lbl('VAS_090_ConfirmedAboveTarget', 'Confirmed Quantity cannot be greater than Target Quantity.'); }
            if (s > target) { return lbl('VAS_090_ScrappedAboveTarget', 'Scrapped Quantity cannot be greater than Target Quantity.'); }
            if (Math.round((c + s) * 1000000) > Math.round(target * 1000000)) { return lbl('VAS_172_ConfScrapExceedsTarget', 'Quantity should not be greater than Target Quantity'); }
            return '';
        }

        function svg(paths) {
            var span = document.createElement('span');
            span.innerHTML = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' + paths + '</svg>';
            return span.firstChild;
        }

        function statusPill(statusKey) {
            var meta = STATUS_META[statusKey] || STATUS_META.drafted;
            var pill = el('span', 'MPC-wc-pill ' + meta.cls, lbl(meta.msgKey, meta.fallback));
            pill.setAttribute('data-msgkey', meta.msgKey); // VAS163 2026-11-27: Sr 48
            return pill;
        }

        function formatDate(value) {
            if (!value) { return ''; }
            var iso = String(value).replace(' ', 'T');
            // VAS163 2026-11-12: bare yyyy-MM-dd parsed by parts as a LOCAL date (no UTC day shift).
            var m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso);
            var date = m ? new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3])) : new Date(iso);
            if (isNaN(date.getTime())) { return String(value); }
            return date.toLocaleDateString(window.navigator.language, { day: '2-digit', month: 'short', year: 'numeric' });
        }

        function formatQty(value) {
            var num = Number(value || 0);
            // VAS163 2026-11-12: quantities shown with at most 4 decimals (rules: 2..4).
            return num.toLocaleString(window.navigator.language, { maximumFractionDigits: 4 });
        }

        function parseResponse(text) {
            var data = text;
            try {
                if (typeof data === 'string' && data.length) { data = JSON.parse(data); }
                if (typeof data === 'string' && data.length) { data = JSON.parse(data); }
            } catch (e) { return null; }
            return data || {};
        }

        /* ---- Build the static widget shell once ---- */
        function build() {
            var head = el('div', 'MPC-wc-head');
            var icon = el('span', 'MPC-wc-ico');
            icon.appendChild(svg('<rect width="8" height="4" x="8" y="2" rx="1" ry="1"/><path d="M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2"/><path d="M12 11h4"/><path d="M12 16h4"/><path d="M8 11h.01"/><path d="M8 16h.01"/>'));
            var titles = el('div', 'MPC-wc-titles');
            titles.appendChild(el('div', 'MPC-wc-title', lbl('VAS_155_WC_Title', 'Waiting Confirmation')));
            titles.appendChild(el('div', 'MPC-wc-subtitle', lbl('VAS_155_WC_Subtitle', 'Drafted and in-progress delivery confirmations')));
            head.appendChild(icon);
            head.appendChild(titles);

            var gridHead = el('div', 'MPC-wc-row MPC-wc-ghead');
            // VAS163 2026-11-27: Sr 48 column headings carry hover text (the "Confirmation" heading clips).
            [['VAS_155_WC_ColConfirmation', 'Confirmation', null], ['VAS_155_WC_ColCustomer', 'Customer', null],
            ['VAS_155_WC_ColLines', 'Lines', null], ['VAS_155_WC_ColStatus', 'Status', 'MPC-wc-col-status']].forEach(function (h) {
                var text = lbl(h[0], h[1]);
                gridHead.appendChild(keyed(tip(el('span', h[2], text), text), h[0]));
            });

            $body = $('<div class="MPC-wc-rows"></div>');

            var foot = el('div', 'MPC-wc-foot');
            $footHelper = $('<span class="MPC-wc-helper"></span>');
            $pager = $('<span class="MPC-wc-pager"></span>');
            var prev = el('button', 'MPC-wc-pgbtn');
            prev.type = 'button';
            prev.appendChild(svg('<path d="m15 18-6-6 6-6"/>'));
            var next = el('button', 'MPC-wc-pgbtn');
            next.type = 'button';
            next.appendChild(svg('<path d="m9 18 6-6-6-6"/>'));
            $pageText = $('<span class="MPC-wc-pgtext"></span>');
            $pager.append(prev, $pageText[0], next);
            $(foot).append($footHelper, $pager);

            $prevButton = $(prev);
            $nextButton = $(next);

            $card = $('<div class="MPC-wc-card"></div>');
            $card.append(head, gridHead, $body[0], foot);
            $root.append($card);

            buildModal();

            $prevButton.on('click.' + eventNamespace, function () {
                if (state.page > 0) { state.page--; renderRows(); }
            });
            $nextButton.on('click.' + eventNamespace, function () {
                var pageCount = Math.max(1, Math.ceil(state.items.length / ROWS_PER_PAGE));
                if (state.page < pageCount - 1) { state.page++; renderRows(); }
            });
            $body.on('click.' + eventNamespace, '.MPC-wc-datarow', function () {
                openModal(Number($(this).attr('data-id')), this);
            });
        }

        function buildModal() {
            var $overlay = $('<div class="MPC-wc-overlay" aria-hidden="true"></div>');
            $modal = $('<div class="MPC-wc-modal" role="dialog" aria-modal="true"></div>');

            var $head = $('<div class="MPC-wc-m-head"></div>');
            $modalTitleGroup = $('<div class="MPC-wc-m-titlegroup"></div>');
            $modalClose = $('<button type="button" class="MPC-wc-m-close"></button>').attr('aria-label', lbl('Close', 'Close'));
            $modalClose.append(svg('<path d="M18 6 6 18"/><path d="m6 6 12 12"/>'));
            $head.append($modalTitleGroup, $modalClose);

            $modalBody = $('<div class="MPC-wc-m-body"></div>');
            $modalFooter = $('<div class="MPC-wc-m-foot"></div>');

            $modal.append($head, $modalBody, $modalFooter);
            $overlay.append($modal);
            $('body').append($overlay);

            $modal.data('overlay', $overlay);

            $modalClose.on('click', closeModal);
        }

        /* ---- Widget list ---- */
        function loadWidgetList() {
            if (listRequest && typeof listRequest.abort === 'function') {
                try { listRequest.abort(); } catch (ignored) { }
            }
            listRequest = (typeof AbortController !== 'undefined') ? new AbortController() : null;

            showRowsMessage(lbl('VAS_155_WC_Loading', 'Loading confirmations...'));

            var url = VIS.Application.contextUrl + 'VAS_155_WaitingConfirmationWidget/GetWaitingConfirmations';
            fetch(url, {
                method: 'GET',
                credentials: 'same-origin',
                signal: listRequest ? listRequest.signal : undefined
            }).then(function (res) { return res.text(); }).then(function (text) {
                var data = parseResponse(text);
                if (!data || data.error) { showRowsError(); return; }
                state.items = data.rows || [];
                state.page = 0;
                state.loaded = true;
                renderRows();
            }).catch(function (err) {
                if (err && err.name === 'AbortError') { return; }
                showRowsError();
            });
        }

        function renderRows() {
            $body.empty();
            var total = state.items.length;

            if (!total) {
                $body.append(el('div', 'MPC-wc-state', lbl('VAS_155_WC_EmptyState', 'No drafted or in-progress confirmations')));
                $footHelper.text('0');
                $pageText.text('1 ' + lbl('VAS_Of', 'of') + ' 1');
                $prevButton.prop('disabled', true);
                $nextButton.prop('disabled', true);
                return;
            }

            var pageCount = Math.max(1, Math.ceil(total / ROWS_PER_PAGE));
            if (state.page > pageCount - 1) { state.page = pageCount - 1; }
            var start = state.page * ROWS_PER_PAGE;
            var slice = state.items.slice(start, start + ROWS_PER_PAGE);

            slice.forEach(function (item) { $body.append(buildRow(item)); });

            var from = start + 1;
            var to = Math.min(total, start + ROWS_PER_PAGE);
            $footHelper.text(lbl('VAS_Showing', 'Showing') + ' ' + formatQty(from) + '–' + formatQty(to) + ' ' + lbl('VAS_Of', 'of') + ' ' + formatQty(total)); // VAS163 2026-11-12: locale counts
            $pageText.text(formatQty(state.page + 1) + ' ' + lbl('VAS_Of', 'of') + ' ' + formatQty(pageCount));
            $prevButton.prop('disabled', state.page <= 0);
            $nextButton.prop('disabled', state.page >= pageCount - 1);
        }

        function buildRow(item) {
            var row = el('button', 'MPC-wc-row MPC-wc-datarow');
            row.type = 'button';
            row.setAttribute('data-id', item.confirmId);

            // VAS163 2026-11-27: Sr 48 every cell carries its full value as hover text.
            row.appendChild(tip(el('span', 'MPC-wc-c-primary', item.confirmNo || ''), item.confirmNo));
            row.appendChild(tip(el('span', 'MPC-wc-c-body', item.customer || ''), item.customer));
            var linesText = formatQty(item.lineCount);
            row.appendChild(tip(el('span', 'MPC-wc-c-body', linesText), linesText));
            var statusCell = el('span', 'MPC-wc-col-status');
            var pill = statusPill(item.status);
            tip(statusCell, pill.textContent);
            statusCell.appendChild(pill);
            row.appendChild(statusCell);

            return row;
        }

        function showRowsMessage(message) {
            $body.empty();
            $body.append(el('div', 'MPC-wc-state', message));
            $footHelper.text('');
            $pageText.text('1 ' + lbl('VAS_Of', 'of') + ' 1');
            $prevButton.prop('disabled', true);
            $nextButton.prop('disabled', true);
        }

        function showRowsError() {
            $body.empty();
            var wrap = el('div', 'MPC-wc-state');
            wrap.appendChild(el('span', null, lbl('VAS_155_WC_LoadError', 'Unable to load confirmations')));
            var retry = el('button', 'MPC-wc-retry', lbl('VAS_155_WC_Retry', 'Retry'));
            retry.type = 'button';
            retry.addEventListener('click', loadWidgetList);
            wrap.appendChild(retry);
            $body.append(wrap);
            $footHelper.text('');
            $pageText.text('1 ' + lbl('VAS_Of', 'of') + ' 1');
            $prevButton.prop('disabled', true);
            $nextButton.prop('disabled', true);
        }

        /* ---- Modal: open / close ---- */
        function openModal(confirmId, triggerEl) {
            lastFocusedEl = triggerEl || null;
            // VAS163 2026-11-27: Sr 54-62 draft (typed line values), qc (quality rows), qcAfterLoad (line to open in Quality Control).
            modalState = { view: 'detail', confirmId: confirmId, header: null, lines: [], linePage: 0, lineIdx: null, locators: [], locatorsLoadedFor: 0, saving: false, error: '', draft: null, qc: null, qcAfterLoad: 0 };

            var $overlay = $modal.data('overlay');
            $overlay.addClass('MPC-wc-open').attr('aria-hidden', 'false');
            $('body').addClass('MPC-wc-body-lock');

            showModalMessage(lbl('VAS_155_WC_Loading', 'Loading confirmations...'));
            loadConfirmationDetail();

            $modalClose.focus();
        }

        function closeModal() {
            if (!$modal) { return; }
            var $overlay = $modal.data('overlay');
            $overlay.removeClass('MPC-wc-open').attr('aria-hidden', 'true');
            $('body').removeClass('MPC-wc-body-lock');
            if (detailRequest && typeof detailRequest.abort === 'function') {
                try { detailRequest.abort(); } catch (ignored) { }
            }
            if (lastFocusedEl && lastFocusedEl.focus) { lastFocusedEl.focus(); }
            lastFocusedEl = null;
        }

        function showModalMessage(message) {
            $modalTitleGroup.empty().append(el('span', 'MPC-wc-m-title', lbl('VAS_155_WC_Title', 'Waiting Confirmation')));
            $modalBody.empty().append(el('div', 'MPC-wc-m-state', message));
            $modalFooter.empty().hide();
        }

        /* ---- Detail view ---- */
        function loadConfirmationDetail() {
            if (detailRequest && typeof detailRequest.abort === 'function') {
                try { detailRequest.abort(); } catch (ignored) { }
            }
            detailRequest = (typeof AbortController !== 'undefined') ? new AbortController() : null;

            var url = VIS.Application.contextUrl + 'VAS_155_WaitingConfirmationWidget/GetConfirmationDetail?confirmId=' + encodeURIComponent(modalState.confirmId);
            fetch(url, {
                method: 'GET',
                credentials: 'same-origin',
                signal: detailRequest ? detailRequest.signal : undefined
            }).then(function (res) { return res.text(); }).then(function (text) {
                var data = parseResponse(text);
                if (!data || data.error || !data.header) {
                    showModalMessage(lbl('VAS_155_WC_NotFound', 'Not found.'));
                    return;
                }
                modalState.header = data.header;
                modalState.lines = data.lines || [];
                modalState.view = 'detail';
                // VAS163 2026-11-27: Sr 62 after Save Line, a line with unanswered quality parameters opens Quality Control.
                var qcLineId = modalState.qcAfterLoad;
                modalState.qcAfterLoad = 0;
                var qcIdx = -1;
                if (qcLineId > 0) {
                    for (var i = 0; i < modalState.lines.length; i++) {
                        if (modalState.lines[i].lineConfirmId === qcLineId) { qcIdx = i; break; }
                    }
                }
                if (qcIdx >= 0) {
                    modalState.lineIdx = qcIdx;
                    modalState.draft = null;
                    openQualityControl();
                    return;
                }
                modalState.linePage = Math.min(modalState.linePage || 0, Math.max(0, Math.ceil(modalState.lines.length / LINES_PER_PAGE) - 1));
                renderModal();
            }).catch(function (err) {
                if (err && err.name === 'AbortError') { return; }
                showModalMessage(lbl('VAS_155_WC_NotFound', 'Not found.'));
            });
        }

        function renderModal() {
            if (modalState.view === 'line') { renderLineView(); }
            else if (modalState.view === 'qc') { renderQualityView(); } // VAS163 2026-11-27: Sr 62
            else { renderDetailView(); }
        }

        function renderDetailView() {
            var header = modalState.header;
            $modalTitleGroup.empty();
            $modalTitleGroup.append(el('span', 'MPC-wc-m-title', (header.confirmNo || '') + ' - ' + (header.doNo || '')));
            $modalTitleGroup.append(statusPill(header.status));

            $modalBody.empty();

            var fieldGrid = el('div', 'MPC-wc-form-grid');
            // VAS163 2026-11-27: Sr 51 Source DO is a link that opens the Delivery Order record.
            var doField = fieldRO(lbl('VAS_155_WC_FieldSourceDO', 'Source DO'), header.doNo, 'VAS_155_WC_FieldSourceDO');
            if (header.doId > 0 && header.doNo) {
                var doValue = doField.querySelector('.MPC-wc-fvalue');
                var doLink = el('button', 'MPC-wc-link', header.doNo);
                doLink.type = 'button';
                tip(doLink, header.doNo);
                doLink.addEventListener('click', function () { openRecordInWindow(header.doId); });
                doValue.textContent = '';
                doValue.appendChild(doLink);
            }
            fieldGrid.appendChild(doField);
            fieldGrid.appendChild(fieldRO(lbl('VAS_155_WC_FieldCustomer', 'Customer'), header.customer, 'VAS_155_WC_FieldCustomer'));
            fieldGrid.appendChild(fieldRO(lbl('VAS_155_WC_FieldWarehouse', 'Warehouse'), header.warehouseName, 'VAS_155_WC_FieldWarehouse'));
            // VAS163 2026-11-27: Sr 61 the field shows the delivery order's Movement Date (label text: message issue on VAS_155_WC_FieldDate).
            fieldGrid.appendChild(fieldRO(lbl('VAS_155_WC_FieldDate', 'Movement Date'), formatDate(header.movementDate || header.confirmDate), 'VAS_155_WC_FieldDate'));
            $modalBody.append(fieldGrid);

            $modalBody.append(el('div', 'MPC-wc-section-title', lbl('VAS_155_WC_LinesSectionTitle', 'Confirmation Lines')));

            var lines = modalState.lines;
            var pageCount = Math.max(1, Math.ceil(lines.length / LINES_PER_PAGE));
            if (modalState.linePage > pageCount - 1) { modalState.linePage = pageCount - 1; }
            var start = modalState.linePage * LINES_PER_PAGE;
            var visible = lines.slice(start, start + LINES_PER_PAGE);

            var list = el('div', 'MPC-wc-lines-list');
            visible.forEach(function (line, i) {
                list.appendChild(buildLineRow(line, start + i));
            });
            $modalBody.append(list);

            if (lines.length > LINES_PER_PAGE) {
                var linePager = el('div', 'MPC-wc-line-pager');
                var lFrom = start + 1;
                var lTo = Math.min(lines.length, start + LINES_PER_PAGE);
                linePager.appendChild(el('span', 'MPC-wc-helper', lbl('VAS_Showing', 'Showing') + ' ' + formatQty(lFrom) + '–' + formatQty(lTo) + ' ' + lbl('VAS_Of', 'of') + ' ' + formatQty(lines.length)));
                var pagerBtns = el('span', 'MPC-wc-pager');
                var lPrev = el('button', 'MPC-wc-pgbtn');
                lPrev.type = 'button';
                lPrev.appendChild(svg('<path d="m15 18-6-6 6-6"/>'));
                lPrev.disabled = modalState.linePage <= 0;
                lPrev.addEventListener('click', function () { if (modalState.linePage > 0) { modalState.linePage--; renderDetailView(); } });
                var lPageText = el('span', 'MPC-wc-pgtext', formatQty(modalState.linePage + 1) + ' ' + lbl('VAS_Of', 'of') + ' ' + formatQty(pageCount));
                var lNext = el('button', 'MPC-wc-pgbtn');
                lNext.type = 'button';
                lNext.appendChild(svg('<path d="m9 18 6-6-6-6"/>'));
                lNext.disabled = modalState.linePage >= pageCount - 1;
                lNext.addEventListener('click', function () { if (modalState.linePage < pageCount - 1) { modalState.linePage++; renderDetailView(); } });
                pagerBtns.appendChild(lPrev);
                pagerBtns.appendChild(lPageText);
                pagerBtns.appendChild(lNext);
                linePager.appendChild(pagerBtns);
                $modalBody.append(linePager);
            }

            if (modalState.error) {
                $modalBody.append(el('div', 'MPC-wc-m-error', modalState.error));
            }

            $modalFooter.empty().show();
            var disputeBtn = el('button', 'MPC-wc-btn MPC-wc-btn-dispute', lbl('VAS_155_WC_MarkInDispute', 'Mark In Dispute'));
            disputeBtn.type = 'button';
            disputeBtn.disabled = header.status === 'dispute' || modalState.saving;
            disputeBtn.addEventListener('click', markInDispute);

            // VAS163 2026-11-27: Sr 60 Complete Confirmation is always available: the core
            // MInOutConfirm.PrepareIt/CompleteIt completes with differences (sets In Dispute and
            // splits the shipment); any document error comes back from the server doc action.
            var completeBtn = el('button', 'MPC-wc-btn MPC-wc-btn-complete', lbl('VAS_155_WC_CompleteConfirmation', 'Complete Confirmation'));
            completeBtn.type = 'button';
            completeBtn.setAttribute('data-msgkey', 'VAS_155_WC_CompleteConfirmation');
            completeBtn.disabled = lines.length === 0 || modalState.saving;
            completeBtn.addEventListener('click', completeConfirmation);

            $modalFooter.append(disputeBtn, completeBtn);

            $modalBody.find('.MPC-wc-line-row').on('click', function () {
                openLineReview(Number($(this).attr('data-idx')));
            });
        }

        function fieldRO(label, value, msgKey) {
            var wrap = el('div', 'MPC-wc-field-ro');
            // VAS163 2026-11-27: Sr 48 / 50 label records its key; label and value carry hover text.
            wrap.appendChild(keyed(tip(el('div', 'MPC-wc-flabel', label), label), msgKey));
            var hasValue = value != null && String(value).trim();
            wrap.appendChild(tip(el('div', 'MPC-wc-fvalue', hasValue ? value : '—'), hasValue ? value : null));
            return wrap;
        }

        function buildLineRow(line, idx) {
            var row = el('button', 'MPC-wc-line-row');
            row.type = 'button';
            row.setAttribute('data-idx', idx);

            var left = el('span', 'MPC-wc-line-left');
            left.appendChild(el('span', 'MPC-wc-line-name', lbl('VAS_155_WC_LinePrefix', 'Line') + ' ' + line.lineNo));
            left.appendChild(tip(el('span', 'MPC-wc-line-meta', line.productName || ''), line.productName)); // VAS163 2026-11-27: Sr 48

            var right = el('span', 'MPC-wc-line-right');
            right.appendChild(el('span', 'MPC-wc-line-qty', formatQty(line.confirmedQty) + ' / ' + formatQty(line.targetQty)));
            var pill = el('span', 'MPC-wc-pill ' + (line.matched ? 'MPC-wc-pill-matched' : 'MPC-wc-pill-pending'),
                lbl(line.matched ? 'VAS_155_WC_Matched' : 'VAS_155_WC_Pending', line.matched ? 'Matched' : 'Pending'));
            right.appendChild(pill);
            right.appendChild(svg('<path d="m9 18 6-6-6-6"/>'));

            row.appendChild(left);
            row.appendChild(right);
            return row;
        }

        /* ---- Line review state ---- */
        function openLineReview(idx) {
            modalState.view = 'line';
            modalState.lineIdx = idx;
            modalState.error = '';
            modalState.draft = null; // VAS163 2026-11-27: Sr 54-57 fresh form values for this line
            modalState.qc = null;
            renderModal();

            var warehouseId = modalState.header ? modalState.header.warehouseId : 0;
            if (warehouseId > 0 && modalState.locatorsLoadedFor !== warehouseId) {
                loadLocators(warehouseId);
            }
        }

        function loadLocators(warehouseId) {
            if (locatorsRequest && typeof locatorsRequest.abort === 'function') {
                try { locatorsRequest.abort(); } catch (ignored) { }
            }
            locatorsRequest = (typeof AbortController !== 'undefined') ? new AbortController() : null;

            var url = VIS.Application.contextUrl + 'VAS_155_WaitingConfirmationWidget/GetWarehouseLocators?warehouseId=' + encodeURIComponent(warehouseId);
            fetch(url, {
                method: 'GET',
                credentials: 'same-origin',
                signal: locatorsRequest ? locatorsRequest.signal : undefined
            }).then(function (res) { return res.text(); }).then(function (text) {
                var data = parseResponse(text);
                modalState.locators = (data && data.rows) || [];
                modalState.locatorsLoadedFor = warehouseId;
                if (modalState.view === 'line') { renderLineView(); }
            }).catch(function () { /* locator list is best-effort; select stays empty */ });
        }

        function renderLineView() {
            var line = modalState.lines[modalState.lineIdx];
            if (!line) { modalState.view = 'detail'; renderModal(); return; }

            $modalTitleGroup.empty();
            $modalTitleGroup.append(el('span', 'MPC-wc-m-title', lbl('VAS_155_WC_ReviewLineTitle', 'Review Confirmation Line')));

            $modalBody.empty();

            var backLink = el('button', 'MPC-wc-back-link');
            backLink.type = 'button';
            backLink.appendChild(svg('<path d="m15 18-6-6 6-6"/>'));
            backLink.appendChild(document.createTextNode(lbl('VAS_155_WC_BackTo', 'Back to') + ' ' + (modalState.header.confirmNo || '')));
            backLink.setAttribute('data-msgkey', 'VAS_155_WC_BackTo');
            backLink.addEventListener('click', function () {
                modalState.view = 'detail';
                modalState.lineIdx = null;
                modalState.error = '';
                modalState.draft = null; // VAS163 2026-11-27: Sr 54-57
                renderModal();
            });
            $modalBody.append(backLink);

            var grid = el('div', 'MPC-wc-line-form-grid');

            // VAS163 2026-11-27: Sr 54-57 typed values survive re-renders (saving, locator load, errors).
            var draft = modalState.draft || {
                scrapLocatorId: line.scrapLocatorId || 0,
                confirmedQty: String(line.confirmedQty != null ? line.confirmedQty : 0),
                scrappedQty: String(line.scrappedQty != null ? line.scrappedQty : 0),
                description: line.description || ''
            };
            modalState.draft = draft;
            var precision = Math.max(0, Number(line.uomPrecision) || 0);
            // VAS163 2026-11-27: Sr 57 input step follows the line UOM's StdPrecision (0 -> whole numbers).
            var qtyStep = precision > 0 ? (1 / Math.pow(10, precision)).toFixed(precision) : '1';

            grid.appendChild(fieldRO(lbl('VAS_155_WC_FieldDOLine', 'DO Line'), line.lineNo + ' - ' + (line.productName || ''), 'VAS_155_WC_FieldDOLine'));
            grid.appendChild(fieldRO(lbl('VAS_155_WC_FieldUom', 'UOM'), line.uomName, 'VAS_155_WC_FieldUom'));
            grid.appendChild(fieldRO(lbl('VAS_155_WC_FieldAttrSetInstance', 'Attribute Set Instance'), line.attributeSetInstance, 'VAS_155_WC_FieldAttrSetInstance'));

            function fieldLabel(key, fallback, suffix) {
                var text = lbl(key, fallback);
                return keyed(tip(el('div', 'MPC-wc-flabel', text + (suffix || '')), text), key);
            }

            var locatorField = el('div', 'MPC-wc-field');
            locatorField.appendChild(fieldLabel('VAS_155_WC_FieldScrapLocator', 'Scrap Locator'));
            var locatorSelect = document.createElement('select');
            locatorSelect.className = 'MPC-wc-select';
            var emptyOpt = document.createElement('option');
            emptyOpt.value = '0';
            emptyOpt.textContent = lbl('VAS_155_WC_SelectLocator', 'Select a locator');
            emptyOpt.setAttribute('data-msgkey', 'VAS_155_WC_SelectLocator');
            locatorSelect.appendChild(emptyOpt);
            var hasCurrent = false;
            modalState.locators.forEach(function (loc) {
                var opt = document.createElement('option');
                opt.value = String(loc.locatorId);
                opt.textContent = loc.locatorName;
                opt.title = loc.locatorName;
                if (Number(loc.locatorId) === Number(draft.scrapLocatorId)) { hasCurrent = true; }
                locatorSelect.appendChild(opt);
            });
            // Keep the saved locator selectable even before / without the list.
            if (!hasCurrent && Number(draft.scrapLocatorId) > 0 && Number(draft.scrapLocatorId) === Number(line.scrapLocatorId) && line.scrapLocatorValue) {
                var curOpt = document.createElement('option');
                curOpt.value = String(line.scrapLocatorId);
                curOpt.textContent = line.scrapLocatorValue;
                locatorSelect.appendChild(curOpt);
            }
            locatorSelect.value = String(draft.scrapLocatorId || 0);
            if (locatorSelect.selectedIndex >= 0) { tip(locatorSelect, locatorSelect.options[locatorSelect.selectedIndex].textContent); }
            locatorField.appendChild(locatorSelect);
            grid.appendChild(locatorField);

            grid.appendChild(fieldRO(lbl('VAS_155_WC_FieldTargetQty', 'Target Quantity'), formatQty(line.targetQty), 'VAS_155_WC_FieldTargetQty'));

            function qtyInput(value) {
                var input = document.createElement('input');
                input.type = 'number';
                input.min = '0';
                input.step = qtyStep;
                input.className = 'MPC-wc-input';
                input.value = value;
                return input;
            }

            var confirmedField = el('div', 'MPC-wc-field');
            confirmedField.appendChild(fieldLabel('VAS_155_WC_FieldConfirmedQty', 'Confirmed Quantity', ' *'));
            var confirmedInput = qtyInput(draft.confirmedQty);
            confirmedField.appendChild(confirmedInput);
            grid.appendChild(confirmedField);

            var diffField = el('div', 'MPC-wc-field MPC-wc-field-ro');
            diffField.appendChild(fieldLabel('VAS_155_WC_FieldDifference', 'Difference'));
            var diffValue = el('div', 'MPC-wc-fvalue', '');
            diffField.appendChild(diffValue);
            grid.appendChild(diffField);

            var scrappedField = el('div', 'MPC-wc-field');
            scrappedField.appendChild(fieldLabel('VAS_155_WC_FieldScrappedQty', 'Scrapped Quantity'));
            var scrappedInput = qtyInput(draft.scrappedQty);
            scrappedField.appendChild(scrappedInput);
            grid.appendChild(scrappedField);

            var descField = el('div', 'MPC-wc-field MPC-wc-field-wide');
            descField.appendChild(fieldLabel('VAS_155_WC_FieldDescription', 'Description'));
            var descArea = document.createElement('textarea');
            descArea.className = 'MPC-wc-textarea';
            descArea.maxLength = 255; // VAS163 2026-11-27: Sr 53 M_InOutLineConfirm.Description is 255 characters.
            descArea.value = draft.description;
            descField.appendChild(descArea);
            grid.appendChild(descField);

            $modalBody.append(grid);

            // VAS163 2026-11-27: Sr 54-57 on-screen info message for the quantity rules (live).
            var infoBox = el('div', 'MPC-wc-m-info');
            infoBox.setAttribute('role', 'status');
            infoBox.style.display = 'none';
            $modalBody.append(infoBox);

            function showQtyInfo(message) {
                infoBox.textContent = message || '';
                infoBox.style.display = message ? '' : 'none';
            }

            // VAS163 2026-11-27: Sr 56 Difference = Target - Confirmed - Scrapped, recalculated live.
            function updateDiff() {
                draft.confirmedQty = confirmedInput.value;
                draft.scrappedQty = scrappedInput.value;
                var t = Number(line.targetQty) || 0;
                var c = parseFloat(confirmedInput.value) || 0;
                var s = parseFloat(scrappedInput.value) || 0;
                var diffText = formatQty(Math.round((t - c - s) * 1000000) / 1000000);
                diffValue.textContent = diffText;
                diffValue.title = diffText;
                var message = validateLineQty(line, confirmedInput.value, scrappedInput.value);
                showQtyInfo(message);
                return message;
            }
            confirmedInput.addEventListener('input', updateDiff);
            scrappedInput.addEventListener('input', updateDiff);
            locatorSelect.addEventListener('change', function () {
                draft.scrapLocatorId = Number(locatorSelect.value) || 0;
                locatorSelect.title = locatorSelect.selectedIndex >= 0 ? locatorSelect.options[locatorSelect.selectedIndex].textContent : '';
            });
            descArea.addEventListener('input', function () { draft.description = descArea.value; });
            updateDiff();

            if (modalState.error) {
                $modalBody.append(el('div', 'MPC-wc-m-error', modalState.error));
            }

            $modalFooter.empty().show();
            // VAS163 2026-11-27: Sr 62 Quality Control entry point when the line has VA010 quality parameters.
            if (Number(line.qcCount) > 0) {
                var qcBtn = el('button', 'MPC-wc-btn MPC-wc-btn-dispute', lbl('VAS_090_QualityControl', 'Quality Control'));
                qcBtn.type = 'button';
                qcBtn.setAttribute('data-msgkey', 'VAS_090_QualityControl');
                qcBtn.disabled = modalState.saving;
                qcBtn.addEventListener('click', function () { openQualityControl(); });
                $modalFooter.append(qcBtn);
            }
            var saveKey = modalState.saving ? 'VAS_155_WC_Saving' : 'VAS_155_WC_SaveLine';
            var saveBtn = el('button', 'MPC-wc-btn MPC-wc-btn-complete', lbl(saveKey, modalState.saving ? 'Saving...' : 'Save Line'));
            saveBtn.type = 'button';
            saveBtn.setAttribute('data-msgkey', saveKey);
            saveBtn.disabled = modalState.saving;
            saveBtn.addEventListener('click', function () {
                // VAS163 2026-11-27: Sr 54-57 block Save Line while a quantity rule is broken.
                var message = updateDiff();
                if (message) { showQtyInfo(message); return; }
                saveLine(line, {
                    scrapLocatorId: Number(locatorSelect.value) || 0,
                    confirmedQty: confirmedInput.value,
                    scrappedQty: scrappedInput.value,
                    description: descArea.value
                });
            });
            $modalFooter.append(saveBtn);
        }

        /* ---- Quality Control state (VAS163 2026-11-27: Sr 62) ---- */
        function openQualityControl() {
            var line = modalState.lines[modalState.lineIdx];
            if (!line) { return; }
            modalState.view = 'qc';
            modalState.qc = null;
            modalState.error = '';
            renderModal();

            if (qcRequest && typeof qcRequest.abort === 'function') {
                try { qcRequest.abort(); } catch (ignored) { }
            }
            qcRequest = (typeof AbortController !== 'undefined') ? new AbortController() : null;

            var url = VIS.Application.contextUrl + 'VAS_155_WaitingConfirmationWidget/GetLineQualityParameters?lineConfirmId=' + encodeURIComponent(line.lineConfirmId);
            fetch(url, {
                method: 'GET',
                credentials: 'same-origin',
                signal: qcRequest ? qcRequest.signal : undefined
            }).then(function (res) { return res.text(); }).then(function (text) {
                var data = parseResponse(text);
                if (!data || data.error) {
                    modalState.qc = [];
                    modalState.error = (data && (data.message || data.error)) || lbl('VAS_155_WC_NotFound', 'Not found.');
                } else {
                    modalState.qc = data.rows || [];
                }
                if (modalState.view === 'qc') { renderQualityView(); }
            }).catch(function (err) {
                if (err && err.name === 'AbortError') { return; }
                modalState.qc = [];
                modalState.error = lbl('VAS_155_WC_NotFound', 'Not found.');
                if (modalState.view === 'qc') { renderQualityView(); }
            });
        }

        function renderQualityView() {
            var line = modalState.lines[modalState.lineIdx];
            if (!line) { modalState.view = 'detail'; renderModal(); return; }

            $modalTitleGroup.empty();
            $modalTitleGroup.append(keyed(el('span', 'MPC-wc-m-title', lbl('VAS_090_QualityControl', 'Quality Control')), 'VAS_090_QualityControl'));

            $modalBody.empty();
            var backLink = el('button', 'MPC-wc-back-link');
            backLink.type = 'button';
            backLink.appendChild(svg('<path d="m15 18-6-6 6-6"/>'));
            backLink.appendChild(document.createTextNode(lbl('VAS_155_WC_BackTo', 'Back to') + ' ' + lbl('VAS_155_WC_LinePrefix', 'Line') + ' ' + line.lineNo));
            backLink.setAttribute('data-msgkey', 'VAS_155_WC_BackTo, VAS_155_WC_LinePrefix');
            backLink.addEventListener('click', function () {
                modalState.view = 'line';
                modalState.error = '';
                renderModal();
            });
            $modalBody.append(backLink);

            var lineTitle = line.lineNo + ' - ' + (line.productName || '');
            $modalBody.append(tip(el('div', 'MPC-wc-section-title', lineTitle), lineTitle));

            var rows = modalState.qc;
            if (rows === null) {
                $modalBody.append(keyed(el('div', 'MPC-wc-m-state', lbl('VAS_155_WC_LinesLoading', 'Loading confirmation lines...')), 'VAS_155_WC_LinesLoading'));
                $modalFooter.empty().hide();
                return;
            }

            var selects = [];
            if (!rows.length) {
                $modalBody.append(keyed(el('div', 'MPC-wc-m-state', lbl('VAS_155_WC_NoQualityParams', 'No quality parameters for this line.')), 'VAS_155_WC_NoQualityParams'));
            } else {
                var table = el('div', 'MPC-wc-qc-table');
                var head = el('div', 'MPC-wc-qc-row MPC-wc-qc-head');
                [['VAS_104_TestParameter', 'Test Parameter'], ['VAS_104_AcceptableValue', 'Acceptable Value'], ['VAS_104_ActualValue', 'Actual Value']].forEach(function (h) {
                    var text = lbl(h[0], h[1]);
                    head.appendChild(keyed(tip(el('span', null, text), text), h[0]));
                });
                table.appendChild(head);

                rows.forEach(function (r) {
                    var row = el('div', 'MPC-wc-qc-row');
                    row.appendChild(tip(el('span', 'MPC-wc-qc-cell', r.testParameter || '—'), r.testParameter));
                    row.appendChild(tip(el('span', 'MPC-wc-qc-cell', r.acceptableValue || '—'), r.acceptableValue));
                    var sel = document.createElement('select');
                    sel.className = 'MPC-wc-select MPC-wc-qc-select';
                    var none = document.createElement('option');
                    none.value = '0';
                    none.textContent = lbl('VAS_Select', 'Select');
                    none.setAttribute('data-msgkey', 'VAS_Select');
                    sel.appendChild(none);
                    (r.options || []).forEach(function (o) {
                        var opt = document.createElement('option');
                        opt.value = String(o.valueId);
                        opt.textContent = o.valueName;
                        opt.title = o.valueName;
                        sel.appendChild(opt);
                    });
                    sel.value = String(r.draftValueId != null ? r.draftValueId : (r.actualValueId || 0));
                    if (sel.selectedIndex < 0) { sel.value = '0'; }
                    tip(sel, sel.options[sel.selectedIndex].textContent);
                    sel.addEventListener('change', function () {
                        r.draftValueId = Number(sel.value) || 0;
                        sel.title = sel.options[sel.selectedIndex].textContent;
                    });
                    sel.disabled = !!modalState.saving;
                    var cell = el('span', 'MPC-wc-qc-cell');
                    cell.appendChild(sel);
                    row.appendChild(cell);
                    table.appendChild(row);
                    selects.push({ row: r, select: sel });
                });
                $modalBody.append(table);
            }

            if (modalState.error) {
                $modalBody.append(el('div', 'MPC-wc-m-error', modalState.error));
            }

            $modalFooter.empty().show();
            if (rows.length) {
                var saveKey = modalState.saving ? 'VAS_155_WC_Saving' : 'VAS_Save';
                var saveBtn = el('button', 'MPC-wc-btn MPC-wc-btn-complete', lbl(saveKey, modalState.saving ? 'Saving...' : 'Save'));
                saveBtn.type = 'button';
                saveBtn.setAttribute('data-msgkey', saveKey);
                saveBtn.disabled = !!modalState.saving;
                saveBtn.addEventListener('click', function () {
                    var values = [];
                    selects.forEach(function (s) {
                        var id = Number(s.select.value) || 0;
                        if (id > 0) { values.push({ qaRecordId: s.row.qaRecordId, actualValueId: id }); }
                    });
                    saveQualityValues(line, values);
                });
                $modalFooter.append(saveBtn);
            }
        }

        function saveQualityValues(line, values) {
            if (modalState.saving) { return; }
            if (!values.length) {
                modalState.error = lbl('VAS_155_WC_QcSelectValue', 'Select an Actual Value for at least one quality parameter.');
                renderQualityView();
                return;
            }
            modalState.saving = true;
            modalState.error = '';
            renderQualityView();

            var url = VIS.Application.contextUrl + 'VAS_155_WaitingConfirmationWidget/SaveLineQualityParameters';
            fetch(url, {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8' },
                body: 'lineConfirmId=' + encodeURIComponent(line.lineConfirmId) + '&values=' + encodeURIComponent(JSON.stringify(values))
            }).then(function (res) { return res.text(); }).then(function (text) {
                var data = parseResponse(text);
                modalState.saving = false;
                if (!data || data.error || data.success === false) {
                    modalState.error = (data && (data.message || data.error)) || lbl('VAS_155_WC_SaveFailed', 'Save failed.');
                    renderQualityView();
                    return;
                }
                var missing = 0;
                (modalState.qc || []).forEach(function (r) {
                    if (r.draftValueId != null) { r.actualValueId = r.draftValueId; r.draftValueId = null; }
                    if (!(Number(r.actualValueId) > 0)) { missing++; }
                });
                line.qcMissing = missing;
                modalState.view = 'line';
                modalState.error = '';
                renderModal();
            }).catch(function () {
                modalState.saving = false;
                modalState.error = lbl('VAS_155_WC_SaveFailed', 'Save failed.');
                renderQualityView();
            });
        }

        /* ---- Actions ---- */
        function saveLine(line, values) {
            modalState.saving = true;
            modalState.error = '';
            renderLineView();

            var url = VIS.Application.contextUrl + 'VAS_155_WaitingConfirmationWidget/SaveConfirmationLine';
            var body = 'lineConfirmId=' + encodeURIComponent(line.lineConfirmId) +
                '&scrapLocatorId=' + encodeURIComponent(values.scrapLocatorId) +
                '&confirmedQty=' + encodeURIComponent(values.confirmedQty) +
                '&scrappedQty=' + encodeURIComponent(values.scrappedQty) +
                '&description=' + encodeURIComponent(values.description);

            fetch(url, {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8' },
                body: body
            }).then(function (res) { return res.text(); }).then(function (text) {
                var data = parseResponse(text);
                modalState.saving = false;
                if (!data || data.error || data.success === false) {
                    modalState.error = (data && (data.message || data.error)) || lbl('VAS_155_WC_SaveFailed', 'Save failed.');
                    renderLineView();
                    return;
                }
                modalState.view = 'detail';
                modalState.lineIdx = null;
                modalState.draft = null;
                // VAS163 2026-11-27: Sr 62 open Quality Control next when this line still has unanswered quality parameters.
                if (Number(line.qcMissing) > 0) { modalState.qcAfterLoad = line.lineConfirmId; }
                loadConfirmationDetail();
            }).catch(function () {
                modalState.saving = false;
                modalState.error = lbl('VAS_155_WC_SaveFailed', 'Save failed.');
                renderLineView();
            });
        }

        function markInDispute() {
            if (modalState.saving) { return; }
            modalState.saving = true;
            modalState.error = '';
            renderDetailView();

            var url = VIS.Application.contextUrl + 'VAS_155_WaitingConfirmationWidget/MarkInDispute';
            fetch(url, {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8' },
                body: 'confirmId=' + encodeURIComponent(modalState.confirmId)
            }).then(function (res) { return res.text(); }).then(function (text) {
                var data = parseResponse(text);
                modalState.saving = false;
                if (!data || data.error || data.success === false) {
                    modalState.error = (data && (data.message || data.error)) || lbl('VAS_155_WC_SaveFailed', 'Save failed.');
                    renderDetailView();
                    return;
                }
                loadConfirmationDetail();
                loadWidgetList();
            }).catch(function () {
                modalState.saving = false;
                modalState.error = lbl('VAS_155_WC_SaveFailed', 'Save failed.');
                renderDetailView();
            });
        }

        function completeConfirmation() {
            if (modalState.saving) { return; }
            modalState.saving = true;
            modalState.error = '';
            renderDetailView();

            var url = VIS.Application.contextUrl + 'VAS_155_WaitingConfirmationWidget/CompleteConfirmation';
            fetch(url, {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8' },
                body: 'confirmId=' + encodeURIComponent(modalState.confirmId)
            }).then(function (res) { return res.text(); }).then(function (text) {
                var data = parseResponse(text);
                modalState.saving = false;
                if (!data || data.error || data.success === false) {
                    modalState.error = (data && (data.message || data.error)) || lbl('VAS_155_WC_SaveFailed', 'Save failed.');
                    renderDetailView();
                    return;
                }
                closeModal();
                loadWidgetList();
            }).catch(function () {
                modalState.saving = false;
                modalState.error = lbl('VAS_155_WC_SaveFailed', 'Save failed.');
                renderDetailView();
            });
        }

        /* ---- Lifecycle ---- */
        this.Initalize = function () {
            build();
            loadWidgetList();
        };

        this.refreshWidget = function () {
            closeModal();
            loadWidgetList();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            closeModal();
            if (listRequest && typeof listRequest.abort === 'function') { try { listRequest.abort(); } catch (ignored) { } }
            if (detailRequest && typeof detailRequest.abort === 'function') { try { detailRequest.abort(); } catch (ignored) { } }
            if (locatorsRequest && typeof locatorsRequest.abort === 'function') { try { locatorsRequest.abort(); } catch (ignored) { } }
            if (qcRequest && typeof qcRequest.abort === 'function') { try { qcRequest.abort(); } catch (ignored) { } } // VAS163 2026-11-27: Sr 62
            $root.off('.' + eventNamespace);
            $body.off('.' + eventNamespace);
            if ($modal) {
                var $overlay = $modal.data('overlay');
                if ($overlay) { $overlay.remove(); }
                $modal = null;
            }
            $('body').removeClass('MPC-wc-body-lock');
            $root.remove();
            state.items = [];
        };
    };

    VAS.VAS_155_WaitingConfirmationWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_155_WaitingConfirmationWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_155_WaitingConfirmationWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_155_WaitingConfirmationWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_155_WaitingConfirmationWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_155_WaitingConfirmationWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
