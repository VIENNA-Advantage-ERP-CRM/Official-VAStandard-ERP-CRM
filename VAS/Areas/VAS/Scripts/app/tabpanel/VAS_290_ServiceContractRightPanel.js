/********************************************************
 * Module Name    : CRM Extension VAS
 * Purpose        : Service Contract Right Detail Panel — client logic
 * Employee Code  : VAI154
 * Date           : 17-Sep-2026
 ******************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_290_ServiceContractRightPanel = function () {
        this.frame;
        this.windowNo;
        this.listener;

        var $self          = this;
        var $root          = null;
        var widgetID       = null;
        var currentCtrId   = 0;
        var pendingXhr     = {};

        // Schedules client-side pagination (6 rows per page as per HTML design)
        var SCHED_PER_PAGE = 6;
        var schedPage      = 0;

        // Section open/closed state — all open by default per requirement
        var sectionsOpen = {
            customer:        true,
            contractDetails: true,
            productPricing:  true,
            billing:         true,
            renewal:         true,
            schedule:        true
        };

        // Per-API-call state: { loading, error, data, loaded }
        var sectionState = {};
        var SECTIONS = ['header', 'schedules'];
        for (var _si = 0; _si < SECTIONS.length; _si++) {
            sectionState[SECTIONS[_si]] = { loading: false, error: null, data: null, loaded: false };
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        function esc(v) {
            return String(v == null ? '' : v)
                .replace(/&/g, '&amp;').replace(/</g, '&lt;')
                .replace(/>/g, '&gt;').replace(/"/g, '&quot;')
                .replace(/'/g, '&#039;');
        }

        function msg(k) {
            var v = (VIS && VIS.Msg && VIS.Msg.getMsg) ? VIS.Msg.getMsg(k) : '';
            // VIS.Msg.getMsg returns '[KeyName]' when AD_Message row is missing — fall back to key
            if (!v || (v.charAt(0) === '[' && v.charAt(v.length - 1) === ']')) return k;
            return v;
        }

        function toNum(v) { var n = Number(v); return isFinite(n) ? n : 0; }

        // Parse a YYYY-MM-DD string as a local calendar date (avoids UTC timezone shift)
        function parseIsoDate(value) {
            if (!value) return null;
            var parts = String(value).slice(0, 10).split('-').map(Number);
            if (parts.length < 3) return null;
            return new Date(parts[0], parts[1] - 1, parts[2]);
        }

        var MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

        function fmtDate(isoValue) {
            var dt = parseIsoDate(isoValue);
            if (!dt) return '—';
            return ('0' + dt.getDate()).slice(-2) + ' ' + MONTHS[dt.getMonth()] + ' ' + dt.getFullYear();
        }

        function fmtMoney(amount, currencyCode, precision) {
            if (amount == null) return '—';
            var n = toNum(amount);
            var prec = (precision != null && isFinite(precision)) ? Math.max(0, parseInt(precision, 10)) : 2;
            if (currencyCode) {
                try {
                    return new Intl.NumberFormat(undefined, {
                        style: 'currency',
                        currency: String(currencyCode),
                        minimumFractionDigits: prec,
                        maximumFractionDigits: prec,
                        currencyDisplay: 'symbol'
                    }).format(n);
                } catch (e) { /* fall through to manual format */ }
            }
            // Manual fallback
            var d = sectionState.header.data;
            var sym = (d && d.currencySymbol) ? String(d.currencySymbol) : '';
            return sym + n.toFixed(prec).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
        }

        // Pending-months calculation from spec (§4.8, calendar-month logic in JS)
        function calculatePendingMonths(endDateValue) {
            if (!endDateValue) return null;
            var endDate = parseIsoDate(endDateValue);
            if (!endDate) return null;
            var today = new Date();
            today = new Date(today.getFullYear(), today.getMonth(), today.getDate());
            if (endDate < today) return 0;
            var months = (endDate.getFullYear() - today.getFullYear()) * 12;
            months += endDate.getMonth() - today.getMonth();
            if (endDate.getDate() < today.getDate()) { months -= 1; }
            return Math.max(0, months);
        }

        // Billing summary derived from already-loaded schedules (avoids a third DB query)
        function getBillingSummary(schedules) {
            var count = schedules ? schedules.length : 0;
            var invoicedCount = schedules
                ? schedules.filter(function (r) { return r.cInvoiceId != null && r.cInvoiceId !== 0; }).length
                : 0;
            var status = 'VAS_290_NoSchedule';
            if (count > 0 && invoicedCount === count) status = 'VAS_290_FullyBilled';
            else if (invoicedCount > 0)               status = 'VAS_290_PartiallyBilled';
            else if (count > 0)                       status = 'VAS_290_NotYetBilled';
            return { count: count, invoicedCount: invoicedCount, status: status };
        }

        // ── SVG icon helpers (inline SVG for zero dependency) ──────────────────
        var ICONS = {
            contract: '<path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5"/><path d="M9 13h6M9 17h4"/>',
            user:     '<circle cx="12" cy="8" r="3.5"/><path d="M5 20a7 7 0 0 1 14 0"/>',
            cal:      '<rect x="3" y="5" width="18" height="16" rx="2"/><path d="M8 3v4M16 3v4M3 11h18"/>',
            cash:     '<rect x="2" y="6" width="20" height="12" rx="2"/><circle cx="12" cy="12" r="2.5"/>',
            pin:      '<path d="M12 21s7-6.2 7-11a7 7 0 1 0-14 0c0 4.8 7 11 7 11z"/><circle cx="12" cy="10" r="2.5"/>',
            refresh:  '<path d="M20 11a8 8 0 1 0-2.3 6.1"/><path d="M20 5v6h-6"/>',
            file:     '<path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5"/>',
            check:    '<path d="m5 12 5 5 9-10"/>',
            clock:    '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
            x:        '<path d="M18 6 6 18M6 6l12 12"/>',
            chevD:    '<path d="m6 9 6 6 6-6"/>',
            chevL:    '<path d="m15 18-6-6 6-6"/>',
            chevR:    '<path d="m9 18 6-6-6-6"/>',
            extLink:  '<path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/><polyline points="15 3 21 3 21 9"/><line x1="10" y1="14" x2="21" y2="3"/>'
        };

        function svgIcon(name) {
            var d = ICONS[name] || '';
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' + d + '</svg>';
        }

        // ── Chip HTML ─────────────────────────────────────────────────────────
        function chip(labelKey, cls) {
            return '<span class="vas_290_sc-chip ' + esc(cls) + '">' + esc(msg(labelKey)) + '</span>';
        }

        function chipLiteral(label, cls) {
            return '<span class="vas_290_sc-chip ' + esc(cls) + '">' + esc(label) + '</span>';
        }

        // ── Section ID helpers ────────────────────────────────────────────────
        function secBodyId(name) { return 'vas_290_sc_sb_' + name + '_' + widgetID; }
        function secHeadId(name) { return 'vas_290_sc_sh_' + name + '_' + widgetID; }
        function secWrapId(name) { return 'vas_290_sc_sw_' + name + '_' + widgetID; }

        // ── AJAX fetch ────────────────────────────────────────────────────────
        function fetchSection(secName, action, extra, callback) {
            if (pendingXhr[secName] && pendingXhr[secName].readyState !== 4) {
                try { pendingXhr[secName].abort(); } catch (e) { /* ignore */ }
            }
            sectionState[secName].loading = true;
            sectionState[secName].error   = null;
            renderSec(secName);

            var postData = $.extend({ contractId: currentCtrId }, extra || {});

            pendingXhr[secName] = $.ajax({
                url:   VIS.Application.contextUrl + 'VAS/VAS_290_ServiceContractRightPanel/' + action,
                type:  'POST',
                data:  postData,
                async: true,
                success: function (raw) {
                    pendingXhr[secName] = null;
                    var parsed = null;
                    try { parsed = (typeof raw === 'string') ? jQuery.parseJSON(raw) : raw; } catch (e) { parsed = null; }
                    sectionState[secName].data    = parsed;
                    sectionState[secName].loading = false;
                    sectionState[secName].loaded  = true;
                    if (callback) callback(parsed);
                    else renderSec(secName);
                },
                error: function (xhr, status) {
                    pendingXhr[secName] = null;
                    if (status === 'abort') return;
                    sectionState[secName].data    = null;
                    sectionState[secName].loading = false;
                    sectionState[secName].error   = status || 'error';
                    renderSec(secName);
                }
            });
        }

        // ── renderSec dispatcher ──────────────────────────────────────────────
        function renderSec(secName) {
            var s = sectionState[secName];
            if (secName === 'header') {
                if (s.loading) {
                    setIdentityHtml(skelLines(4));
                    setSecBodyHtml('customer',        skelLines(3));
                    setSecBodyHtml('contractDetails', skelLines(3));
                    setSecBodyHtml('productPricing',  skelLines(4));
                    setSecBodyHtml('billing',         skelLines(2));
                    setSecBodyHtml('renewal',         skelLines(2));
                    return;
                }
                if (s.error) {
                    setIdentityHtml(errorState(function () { fetchSection('header', 'GetContractOverview', {}, afterHeaderLoad); }));
                    return;
                }
                renderHeaderSections(s.data);
            } else if (secName === 'schedules') {
                if (s.loading) { setSecBodyHtml('schedule', skelLines(3)); return; }
                if (s.error)   { setSecBodyHtml('schedule', errorState(function () { fetchSection('schedules', 'GetContractSchedules', {}, null); })); return; }
                renderScheduleSection(s.data);
            }
        }

        function setIdentityHtml(html) {
            var el = $root ? $root.find('#vas_290_sc_identity_' + widgetID)[0] : null;
            if (el) el.innerHTML = html;
        }

        function setSecBodyHtml(name, html) {
            var el = $root ? $root.find('#' + secBodyId(name))[0] : null;
            if (el) el.innerHTML = html;
        }

        // ── Skeleton lines ────────────────────────────────────────────────────
        function skelLines(count) {
            var h = '';
            for (var i = 0; i < count; i++) {
                var w = (i % 3 === 0) ? '80%' : (i % 3 === 1 ? '60%' : '70%');
                h += '<div class="vas_290_sc-skel" style="width:' + w + ';"></div>';
            }
            return h;
        }

        // ── Error state (inline retry button) ─────────────────────────────────
        function errorState(retryFn) {
            var btnId = 'vas_290_sc_retry_' + widgetID + '_' + Date.now();
            setTimeout(function () {
                var btn = document.getElementById(btnId);
                if (btn) btn.addEventListener('click', function (e) { e.stopPropagation(); retryFn(); });
            }, 0);
            return '<div class="vas_290_sc-error" aria-live="polite">' +
                '<span class="vas_290_sc-errtxt">' + esc(msg('VAS_290_LoadError')) + '</span>' +
                '<button class="vas_290_sc-btn bg" id="' + btnId + '">' + esc(msg('VAS_290_Retry')) + '</button>' +
            '</div>';
        }

        // ── Empty state ───────────────────────────────────────────────────────
        function emptyState(msgKey) {
            return '<div class="vas_290_sc-empty" aria-live="polite">' + esc(msg(msgKey)) + '</div>';
        }

        // ── Field row helper ──────────────────────────────────────────────────
        function frow(label, valueHtml, valueCls, mod) {
            return '<div class="vas_290_sc-frow' + (mod ? ' ' + mod : '') + '">' +
                '<span class="vas_290_sc-f-label">' + esc(label) + '</span>' +
                '<span class="vas_290_sc-f-value' + (valueCls ? ' ' + valueCls : '') + '">' + valueHtml + '</span>' +
            '</div>';
        }

        function frowPlain(label, value, valueCls, mod) {
            return frow(label, esc(value || '—'), valueCls, mod);
        }

        // ── Collapsible section HTML builder ──────────────────────────────────
        function buildSection(name, titleMsgKey, iconName, sumText, bodyHtml) {
            var isOpen = sectionsOpen[name] !== false;
            var headId = secHeadId(name);
            var bodyId = secBodyId(name);
            var wrapId = secWrapId(name);
            return '<div class="vas_290_sc-sec" id="' + wrapId + '">' +
                '<button class="vas_290_sc-sec__head" id="' + headId + '" ' +
                        'aria-expanded="' + isOpen + '" ' +
                        'aria-controls="' + bodyId + '">' +
                    '<span class="vas_290_sc-sec__title">' +
                        svgIcon(iconName) + esc(msg(titleMsgKey)) +
                    '</span>' +
                    '<span class="vas_290_sc-sec__right">' +
                        (sumText ? '<span class="vas_290_sc-sec__sum">' + esc(sumText) + '</span>' : '') +
                        '<svg class="vas_290_sc-sec__chev" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">' + ICONS.chevD + '</svg>' +
                    '</span>' +
                '</button>' +
                '<div class="vas_290_sc-sec__body' + (isOpen ? '' : ' vas_290_sc-sec__body--closed') + '" ' +
                     'id="' + bodyId + '">' +
                    bodyHtml +
                '</div>' +
            '</div>';
        }

        // ── Toggle collapsible section ────────────────────────────────────────
        function toggleSection(name) {
            sectionsOpen[name] = !sectionsOpen[name];
            var headEl = $root ? $root.find('#' + secHeadId(name))[0] : null;
            var bodyEl = $root ? $root.find('#' + secBodyId(name))[0] : null;
            if (!headEl || !bodyEl) return;
            var isOpen = sectionsOpen[name];
            headEl.setAttribute('aria-expanded', String(isOpen));
            if (isOpen) {
                bodyEl.classList.remove('vas_290_sc-sec__body--closed');
            } else {
                bodyEl.classList.add('vas_290_sc-sec__body--closed');
            }
        }

        // ── Wire section toggle buttons (event delegation) ────────────────────
        function wireSectionToggles() {
            var names = ['customer', 'contractDetails', 'productPricing', 'billing', 'renewal', 'schedule'];
            for (var i = 0; i < names.length; i++) {
                (function (n) {
                    var headEl = $root ? $root.find('#' + secHeadId(n))[0] : null;
                    if (!headEl) return;
                    $(headEl).off('click.sc_toggle').on('click.sc_toggle', function (e) {
                        e.stopPropagation();
                        toggleSection(n);
                    });
                }(names[i]));
            }
        }

        // ── Render identity (hero) section ────────────────────────────────────
        function renderIdentity(data) {
            if (!data || data.error === 'not_found') {
                setIdentityHtml(emptyState('VAS_290_NoContractSelected'));
                return;
            }

            var isProcessed = (data.processed === true || data.processed === 'Y');
            var ccy         = data.currencyIsoCode || '';
            var prec        = (data.currencyPrecision != null) ? parseInt(data.currencyPrecision, 10) : 2;

            // Validity in months from start/end dates
            var validityStr = '—';
            var startDt = parseIsoDate(data.startDate);
            var endDt   = parseIsoDate(data.endDate);
            if (startDt && endDt) {
                var mths = (endDt.getFullYear() - startDt.getFullYear()) * 12 + (endDt.getMonth() - startDt.getMonth());
                if (endDt.getDate() >= startDt.getDate()) mths += 1;
                validityStr = Math.max(0, mths) + ' ' + msg('VAS_290_Months');
            }

            var statusLabel = esc(data.docStatusLabel || data.docStatus || '');
            var statusCls   = (data.docStatus === 'CO' || data.processed === true || data.processed === 'Y') ? 'cs' : 'ci';
            var readOnlyChip = isProcessed
                ? '<span class="vas_290_sc-chip ci" style="margin-left:0.5em;">' + esc(msg('VAS_290_ReadOnly')) + '</span>'
                : '';

            var contractTypeTxt = esc(data.contractTypeLabel || (data.contractType === 'AR'
                ? msg('VAS_290_AccountsReceivable') : msg('VAS_290_AccountsPayable')));

            var customerLoc = esc(data.billingLocationName || data.billingLocationAddress || '');

            var html =
                '<div class="vas_290_sc-identity">' +
                    '<div class="vas_290_sc-hero-top">' +
                        '<div style="min-width:0;">' +
                            '<p class="vas_290_sc-hero-no">' + esc(data.documentNo || '') + '</p>' +
                            '<p class="vas_290_sc-hero-ref">' +
                                esc(msg('VAS_290_ContractID')) + ' ' + esc(String(data.id || '')) +
                                (data.orderDocumentNo ? ' · ' + esc(data.orderDocumentNo) : '') +
                            '</p>' +
                            '<div class="vas_290_sc-hero-val">' +
                                '<span class="vas_290_sc-hero-amt" id="vas_290_sc_hero_amt_' + widgetID + '"></span>' +
                                '<span class="vas_290_sc-hero-amt-lbl" id="vas_290_sc_hero_lbl_' + widgetID + '"></span>' +
                            '</div>' +
                        '</div>' +
                        '<div>' +
                            '<p class="vas_290_sc-hero-cust__lbl">' + esc(msg('VAS_290_Customer')) + '</p>' +
                            '<p class="vas_290_sc-hero-cust__name" title="' + esc(data.bPartnerName || '') + '">' + esc(data.bPartnerName || '—') + '</p>' +
                            (customerLoc ? '<p class="vas_290_sc-hero-cust__loc">' + svgIcon('pin') + '<span>' + customerLoc + '</span></p>' : '') +
                        '</div>' +
                        '<div>' +
                            '<span class="vas_290_sc-chip ' + statusCls + '">' + statusLabel + '</span>' +
                            readOnlyChip +
                        '</div>' +
                    '</div>' +
                    '<div class="vas_290_sc-frows">' +
                        frowPlain(msg('VAS_290_StartDate'),    fmtDate(data.startDate)) +
                        frowPlain(msg('VAS_290_EndDate'),      fmtDate(data.endDate)) +
                        frowPlain(msg('VAS_290_Validity'),     validityStr) +
                        frowPlain(msg('VAS_290_ContractType'), contractTypeTxt) +
                    '</div>' +
                    '<div class="vas_290_sc-id-actions">' +
                        '<button class="vas_290_sc-btn bs" id="vas_290_btn_gensched_' + widgetID + '"' +
                                (isProcessed ? ' disabled aria-disabled="true"' : '') + '>' +
                            svgIcon('refresh') + esc(msg('VAS_290_GenerateSchedule')) +
                        '</button>' +
                        '<button class="vas_290_sc-btn bs" id="vas_290_btn_renew_' + widgetID + '"' +
                                (isProcessed ? ' disabled aria-disabled="true"' : '') + '>' +
                            svgIcon('cal') + esc(msg('VAS_290_Renew')) +
                        '</button>' +
                        '<button class="vas_290_sc-btn bd" id="vas_290_btn_cancel_' + widgetID + '"' +
                                (isProcessed ? ' disabled aria-disabled="true"' : '') + '>' +
                            svgIcon('x') + esc(msg('VAS_290_CancelContract')) +
                        '</button>' +
                    '</div>' +
                '</div>';

            setIdentityHtml(html);
            refreshHeroContractValue();

            // Wire action buttons — guard with isProcessed check in handler so
            // a manual DOM bypass cannot trigger actions on processed records
            if (!isProcessed) {
                var btnGen = document.getElementById('vas_290_btn_gensched_' + widgetID);
                if (btnGen) {
                    btnGen.addEventListener('click', function (e) {
                        e.stopPropagation();
                        if (sectionState.header.data && sectionState.header.data.processed !== true) {
                            triggerAction('GenerateSchedule');
                        }
                    });
                }
                var btnRen = document.getElementById('vas_290_btn_renew_' + widgetID);
                if (btnRen) {
                    btnRen.addEventListener('click', function (e) {
                        e.stopPropagation();
                        if (sectionState.header.data && sectionState.header.data.processed !== true) {
                            triggerAction('Renew');
                        }
                    });
                }
                var btnCan = document.getElementById('vas_290_btn_cancel_' + widgetID);
                if (btnCan) {
                    btnCan.addEventListener('click', function (e) {
                        e.stopPropagation();
                        if (sectionState.header.data && sectionState.header.data.processed !== true) {
                            triggerAction('CancelContract');
                        }
                    });
                }
            }
        }

        // ── Refresh contract-value label and amount in the hero header ───────────
        // Called after header loads and again after schedules load.
        // Monthly billing: label = VAS_290_PerScheduleContractTotalValue,
        //                  value = schedule count × lineNetAmt.
        // All other frequencies: label = VAS_290_TotalContractValue,
        //                        value = grandTotal.
        // Null inputs are treated as 0 so the display never shows NaN or "—".
        function refreshHeroContractValue() {
            var d = sectionState.header.data;
            if (!d) return;

            var amtEl = document.getElementById('vas_290_sc_hero_amt_' + widgetID);
            var lblEl = document.getElementById('vas_290_sc_hero_lbl_' + widgetID);
            if (!amtEl || !lblEl) return;

            var ccy      = d.currencyIsoCode || '';
            var prec     = (d.currencyPrecision != null) ? parseInt(d.currencyPrecision, 10) : 2;
            var isMonthly = String(d.frequencyName || '').toLowerCase().indexOf('month') >= 0;

            var displayLabel, displayAmt;

            if (isMonthly) {
                // Monthly: Number of Invoices (schedule count) × Line Amount
                var schedItems = (sectionState.schedules.loaded && sectionState.schedules.data && sectionState.schedules.data.items)
                    ? sectionState.schedules.data.items : [];
                displayLabel = msg('VAS_290_PerScheduleContractTotalValue');
                displayAmt   = fmtMoney(schedItems.length * toNum(d.lineNetAmt), ccy, prec);
            } else {
                // Yearly and all other frequencies: Total Contract Value
                displayLabel = msg('VAS_290_TotalContractValue');
                displayAmt   = fmtMoney(toNum(d.grandTotal), ccy, prec);
            }

            amtEl.textContent = displayAmt;
            lblEl.textContent = displayLabel;
        }

        // ── Trigger an existing Onfinity process action ────────────────────────
        // TODO: Replace with actual Onfinity process invocation using the known
        //       process IDs for Generate Schedule, Renew, and Cancel Contract.
        function triggerAction(actionName) {
            if (VIS && VIS.Msg && VIS.Msg.showMessage) {
                VIS.Msg.showMessage(msg('VAS_290_ActionNotWired') || actionName);
            }
        }

        // ── Zoom to Invoice window ────────────────────────────────────────────
        // Resolution order mirrors SIT01_001:
        //   1. Backend GetWindowIdByTable — reads AD_Table.AD_Window_ID (canonical).
        //      Preferred because ZoomTarget can pick the wrong window when multiple
        //      VA windows share the same underlying table.
        //   2. VIS.ZoomTarget.getZoomAD_Window_ID — client-side fallback.
        //   3. VIS.viewManager.startWindow — opens the resolved window.
        function openInvoice(invoiceId, isSOTrx) {
            if (!invoiceId || +invoiceId <= 0 || !window.VIS) return;

            function startWindow(windowId) {
                if (windowId > 0 && VIS.viewManager && typeof VIS.viewManager.startWindow === 'function') {
                    var q = (VIS.Query && VIS.Query.prototype && typeof VIS.Query.prototype.getEqualQuery === 'function')
                        ? VIS.Query.prototype.getEqualQuery('C_Invoice_ID', +invoiceId)
                        : null;
                    VIS.viewManager.startWindow(windowId, q);
                }
            }

            function zoomFallback() {
                var wid = 0;
                if (VIS.ZoomTarget && typeof VIS.ZoomTarget.getZoomAD_Window_ID === 'function') {
                    try { wid = VIS.ZoomTarget.getZoomAD_Window_ID('C_Invoice', 0, null, !!isSOTrx) || 0; } catch (e) {}
                }
                startWindow(wid);
            }

            $.ajax({
                url:  VIS.Application.contextUrl + 'VAS/VAS_290_ServiceContractRightPanel/GetWindowIdByTable',
                type: 'POST',
                data: { fields: 'C_Invoice' },
                async: true,
                success: function (raw) {
                    var id = 0;
                    try { id = parseInt(typeof raw === 'string' ? JSON.parse(raw) : raw, 10) || 0; } catch (e) {}
                    if (id > 0) { startWindow(id); } else { zoomFallback(); }
                },
                error: function () { zoomFallback(); }
            });
        }

        // ── Render all sections from header data ──────────────────────────────
        function renderHeaderSections(data) {
            renderIdentity(data);
            if (!data || data.error) return;
            renderCustomerSection(data);
            renderContractDetailsSection(data);
            renderProductPricingSection(data);
            renderBillingSection(data);
            renderRenewalSection(data);
        }

        // ── Section 2: Customer Details ───────────────────────────────────────
        function renderCustomerSection(data) {
            var ccy  = esc(data.currencyIsoCode || '');
            var body =
                '<div class="vas_290_sc-frows">' +
                    frowPlain(msg('VAS_290_CustomerName'), data.bPartnerName) +
                    frowPlain(msg('VAS_290_Location'),     data.billingLocationName || data.billingLocationAddress || '—') +
                    frowPlain(msg('VAS_290_AccountType'),  data.contractTypeLabel || (data.contractType === 'AR' ? msg('VAS_290_AccountsReceivable') : msg('VAS_290_AccountsPayable'))) +
                    frowPlain(msg('VAS_290_Currency'),     (data.currencyIsoCode ? data.currencyIsoCode + (data.currencySymbol ? ' (' + data.currencySymbol + ')' : '') : '—')) +
                    frowPlain(msg('VAS_290_PaymentTerm'),  data.paymentTermName) +
                    frowPlain(msg('VAS_290_PriceList'),    data.priceListName) +
                '</div>';

            setSecBodyHtml('customer', body);
            updateSectionSum('customer', esc(data.billingLocationName || data.billingLocationAddress || ''));
        }

        // ── Section 3: Contract Details ───────────────────────────────────────
        function renderContractDetailsSection(data) {
            var body =
                '<div class="vas_290_sc-frows">' +
                    frowPlain(msg('VAS_290_ContractType'),      data.contractTypeLabel || (data.contractType === 'AR' ? msg('VAS_290_AccountsReceivable') : msg('VAS_290_AccountsPayable'))) +
                    frowPlain(msg('VAS_290_ContractReference'),  data.refContract) +
                    (data.refContractDocumentNo ? frowPlain(msg('VAS_290_ReferencedContract'), data.refContractDocumentNo) : '') +
                    frowPlain(msg('VAS_290_StartDate'),          fmtDate(data.startDate)) +
                    frowPlain(msg('VAS_290_EndDate'),            fmtDate(data.endDate)) +
                    frowPlain(msg('VAS_290_SourceOrder'),        data.orderDocumentNo) +
                    frowPlain(msg('VAS_290_OrderLine'),          data.orderLineNo ? (data.orderLineNo + (data.orderDescription ? ' — ' + data.orderDescription : '')) : '—') +
                    frow(msg('VAS_290_Description'), esc(data.description || '—'), '', 'span-full stacked') +
                '</div>';

            setSecBodyHtml('contractDetails', body);
            updateSectionSum('contractDetails', esc(data.refContract || ''));
        }

        // ── Section 4: Product and Pricing ────────────────────────────────────
        function renderProductPricingSection(data) {
            var ccy  = data.currencyIsoCode;
            var prec = (data.currencyPrecision != null) ? parseInt(data.currencyPrecision, 10) : 2;

            var body =
                '<div class="vas_290_sc-frows">' +
                    frowPlain(msg('VAS_290_Product'),        data.productName, '', 'span-full') +
                    frowPlain(msg('VAS_290_UnitOfMeasure'),  data.uomName) +
                    frowPlain(msg('VAS_290_Attribute'),      data.attributeDisplay) +
                    frowPlain(msg('VAS_290_UnitPrice'),      fmtMoney(data.priceActual,    ccy, prec)) +
                    frowPlain(msg('VAS_290_Price'),          fmtMoney(data.priceEntered,   ccy, prec)) +
                    frowPlain(msg('VAS_290_ListPrice'),      fmtMoney(data.priceListAmount, ccy, prec)) +
                    frowPlain(msg('VAS_290_Discount'),       (data.discount != null ? toNum(data.discount).toFixed(2) + '%' : '—')) +
                    frowPlain(msg('VAS_290_TaxAmount'),      fmtMoney(data.taxAmt,         ccy, prec)) +
                    frowPlain(msg('VAS_290_LineNetAmount'),  fmtMoney(data.lineNetAmt,      ccy, prec)) +
                    frowPlain(msg('VAS_290_TotalContractValue'), fmtMoney(data.grandTotal, ccy, prec), 'va', 'span-full') +
                '</div>';

            setSecBodyHtml('productPricing', body);
            updateSectionSum('productPricing', esc(fmtMoney(data.priceActual, ccy, prec)));
        }

        // ── Section 5: Billing ────────────────────────────────────────────────
        function renderBillingSection(data) {
            var ccy       = data.currencyIsoCode;
            var prec      = (data.currencyPrecision != null) ? parseInt(data.currencyPrecision, 10) : 2;
            var schedules = (sectionState.schedules.loaded && sectionState.schedules.data)
                ? (sectionState.schedules.data.items || []) : [];
            var summary   = getBillingSummary(schedules);

            var statusCls = summary.status === 'VAS_290_FullyBilled' ? 'cs'
                : summary.status === 'VAS_290_PartiallyBilled' ? 'cs'
                : summary.status === 'VAS_290_NotYetBilled' ? 'cw'
                : 'cn';

            var body =
                '<div class="vas_290_sc-frows">' +
                    frowPlain(msg('VAS_290_BillingFrequency'), data.frequencyName || '—') +
                    frowPlain(msg('VAS_290_NoOfSchedules'),    String(summary.count)) +
                    frowPlain(msg('VAS_290_TotalInvoiced'),    fmtMoney(data.grandTotal, ccy, prec)) +
                    frow(msg('VAS_290_InvoiceStatus'), chip(summary.status, statusCls)) +
                '</div>';

            setSecBodyHtml('billing', body);
            updateSectionSum('billing', esc(data.frequencyName || ''));
        }

        // ── Section 6: Renewal and Cancellation ──────────────────────────────
        function renderRenewalSection(data) {
            var pendingMonths = calculatePendingMonths(data.endDate);
            var pendingTxt = pendingMonths === null ? '—'
                : pendingMonths === 0 ? '0 ' + msg('VAS_290_Months')
                : pendingMonths + ' ' + msg('VAS_290_Months');

            var renewalCls = (data.renewalTypeLabel || '').toLowerCase().indexOf('auto') >= 0 ? 'vs' : '';
            var cancelDateTxt = data.cancellationDate ? fmtDate(data.cancellationDate) : msg('VAS_290_NotCancelled');
            var cancelDateCls = data.cancellationDate ? '' : 'vm';

            var body =
                '<div class="vas_290_sc-frows">' +
                    frow(msg('VAS_290_RenewalType'),    esc(data.renewalTypeLabel || data.renewalType || '—'), renewalCls) +
                    frowPlain(msg('VAS_290_PendingMonths'),  pendingTxt) +
                    frowPlain(msg('VAS_290_NoticeDays'),     data.cancelBeforeDays != null ? String(data.cancelBeforeDays) : '—') +
                    frow(msg('VAS_290_CancellationDate'), esc(cancelDateTxt), cancelDateCls) +
                '</div>';

            setSecBodyHtml('renewal', body);
            updateSectionSum('renewal', esc(data.renewalTypeLabel || data.renewalType || ''));
        }

        // ── Section 7: Contract Schedule ──────────────────────────────────────
        function renderScheduleSection(schedData) {
            var items = (schedData && schedData.items) ? schedData.items : [];
            var d     = sectionState.header.data;
            var ccy   = d ? d.currencyIsoCode : '';
            var prec  = (d && d.currencyPrecision != null) ? parseInt(d.currencyPrecision, 10) : 2;
            var total = d ? toNum(d.grandTotal) : 0;
            var tax   = d ? toNum(d.taxAmt)     : 0;

            if (items.length === 0) {
                setSecBodyHtml('schedule', '<div class="vas_290_sc-empty-row">' + esc(msg('VAS_290_NoSchedulesAvailable')) + '</div>');
                updateSectionSum('schedule', '');
                // Refresh billing section status now that schedules are known
                if (d) renderBillingSection(d);
                // Update hero label/value — Monthly with 0 schedules should show 0, not stale data
                refreshHeroContractValue();
                return;
            }

            // Refresh billing section with actual schedule data
            if (d) renderBillingSection(d);

            var pages = Math.max(1, Math.ceil(items.length / SCHED_PER_PAGE));
            if (schedPage > pages - 1) schedPage = pages - 1;
            var from  = schedPage * SCHED_PER_PAGE;
            var slice = items.slice(from, from + SCHED_PER_PAGE);

            var statusIcon = { Invoiced: ['check', 't-success'], Due: ['clock', 't-warn'], Scheduled: ['cal', 't-info'] };
            var statusCls  = { Invoiced: 'cs', Due: 'cw', Scheduled: 'cn' };
            var statusMsgKey = { Invoiced: 'VAS_290_Invoiced', Due: 'VAS_290_Due', Scheduled: 'VAS_290_Scheduled' };

            var head =
                '<div class="vas_290_sc-dg-head">' +
                    '<span class="vas_290_sc-cell"></span>' +
                    '<span class="vas_290_sc-cell">' + esc(msg('VAS_290_PeriodStart')) + '</span>' +
                    '<span class="vas_290_sc-cell">' + esc(msg('VAS_290_PeriodEnd')) + '</span>' +
                    '<span class="vas_290_sc-cell rt">' + esc(msg('VAS_290_Amount')) + '</span>' +
                    '<span class="vas_290_sc-cell rt">' + esc(msg('VAS_290_Tax')) + '</span>' +
                    '<span class="vas_290_sc-cell rt">' + esc(msg('VAS_290_Total')) + '</span>' +
                    '<span class="vas_290_sc-cell">' + esc(msg('VAS_290_BillingStatus')) + '</span>' +
                '</div>';

            // isSOTrx determined from header: AR contracts produce Sales Invoices
            var hdrIsSOTrx = (d && d.contractType === 'AR') ? 'Y' : 'N';

            var body = '';
            for (var i = 0; i < slice.length; i++) {
                var r = slice[i];
                var st  = r.billingStatus || 'Scheduled';
                var ico = statusIcon[st]  || ['cal', 't-info'];
                var cls = statusCls[st]   || 'cn';
                var lbl = statusMsgKey[st] || 'VAS_290_Scheduled';

                // Invoiced rows are clickable — they carry the invoice ID and open C_Invoice
                var isInvoiced  = (st === 'Invoiced' && r.cInvoiceId && +r.cInvoiceId > 0);
                var rowLinkAttr = isInvoiced
                    ? ' data-open-invoice="' + esc(r.cInvoiceId) + '" data-sotrx="' + hdrIsSOTrx + '"' +
                      ' tabindex="0" role="button" aria-label="' + esc(msg(lbl)) +
                      (r.invoiceDocumentNo ? ' ' + r.invoiceDocumentNo : '') + '"'
                    : '';
                var rowCls = 'vas_290_sc-dg-row' + (isInvoiced ? ' vas_290_sc-dg-row--link' : '');

                // Status chip: for invoiced rows append a tiny external-link icon
                var chipHtml = chip(lbl, cls);
                if (isInvoiced) {
                    chipHtml += '<span class="vas_290_sc-zoom-ico" aria-hidden="true">' + svgIcon('extLink') + '</span>';
                }

                body +=
                    '<div class="' + rowCls + '"' + rowLinkAttr + '>' +
                        '<span class="vas_290_sc-cell ico ' + ico[1] + '" title="' + esc(msg(lbl)) + '">' + svgIcon(ico[0]) + '</span>' +
                        '<span class="vas_290_sc-cell cp" title="' + esc(fmtDate(r.fromDate)) + '">' + esc(fmtDate(r.fromDate)) + '</span>' +
                        '<span class="vas_290_sc-cell" title="' + esc(fmtDate(r.endDate)) + '">'  + esc(fmtDate(r.endDate))  + '</span>' +
                        '<span class="vas_290_sc-cell rt" title="' + esc(fmtMoney(r.totalAmt,   ccy, prec)) + '">' + esc(fmtMoney(r.totalAmt,   ccy, prec)) + '</span>' +
                        '<span class="vas_290_sc-cell rt" title="' + esc(fmtMoney(r.taxAmt,     ccy, prec)) + '">' + esc(fmtMoney(r.taxAmt,     ccy, prec)) + '</span>' +
                        '<span class="vas_290_sc-cell rt ce" title="' + esc(fmtMoney(r.grandTotal, ccy, prec)) + '">' + esc(fmtMoney(r.grandTotal, ccy, prec)) + '</span>' +
                        '<span class="vas_290_sc-cell chc" title="' + esc(msg(lbl)) + (r.invoiceDocumentNo ? ' · ' + r.invoiceDocumentNo : '') + '">' +
                            chipHtml +
                        '</span>' +
                    '</div>';
            }

            var foot =
                '<div class="vas_290_sc-dg-foot">' +
                    '<span class="vas_290_sc-dg-foot__lbl">' +
                        esc(msg('VAS_290_Total')) + ' · ' + items.length + ' ' + esc(msg('VAS_290_Schedules')) +
                        ' · ' + esc(msg('VAS_290_Tax')) + ' ' + esc(fmtMoney(tax, ccy, prec)) +
                    '</span>' +
                    '<span class="vas_290_sc-dg-foot__total">' + esc(fmtMoney(total, ccy, prec)) + '</span>' +
                '</div>';

            var pager = '';
            if (pages > 1) {
                var pgPrevId = 'vas_290_sc_pgprev_' + widgetID;
                var pgNextId = 'vas_290_sc_pgnext_' + widgetID;
                pager =
                    '<div class="vas_290_sc-pager">' +
                        '<span class="vas_290_sc-pager__info">' +
                            esc(msg('VAS_290_Showing')) + ' ' + (from + 1) + '–' + (from + slice.length) +
                            ' ' + esc(msg('VAS_290_Of')) + ' ' + items.length +
                        '</span>' +
                        '<span class="vas_290_sc-pager__ctl">' +
                            '<button class="vas_290_sc-pgbtn" id="' + pgPrevId + '" aria-label="' + esc(msg('VAS_290_PreviousPage')) + '"' +
                                    (schedPage === 0 ? ' disabled' : '') + '>' +
                                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">' + ICONS.chevL + '</svg>' +
                            '</button>' +
                            '<span>' + (schedPage + 1) + ' / ' + pages + '</span>' +
                            '<button class="vas_290_sc-pgbtn" id="' + pgNextId + '" aria-label="' + esc(msg('VAS_290_NextPage')) + '"' +
                                    (schedPage >= pages - 1 ? ' disabled' : '') + '>' +
                                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">' + ICONS.chevR + '</svg>' +
                            '</button>' +
                        '</span>' +
                    '</div>';
                setTimeout(function () {
                    var prev = document.getElementById(pgPrevId);
                    var next = document.getElementById(pgNextId);
                    if (prev) prev.addEventListener('click', function (e) { e.stopPropagation(); schedPage--; renderSec('schedules'); });
                    if (next) next.addEventListener('click', function (e) { e.stopPropagation(); schedPage++; renderSec('schedules'); });
                }, 0);
            }

            setSecBodyHtml('schedule', head + body + foot + pager);
            updateSectionSum('schedule', (d && d.frequencyName ? d.frequencyName + ' · ' : '') + items.length + ' ' + msg('VAS_290_Schedules'));
            // Update hero label/value with final schedule count (Monthly: count × lineNetAmt)
            refreshHeroContractValue();
        }

        // ── Update section summary text ───────────────────────────────────────
        function updateSectionSum(name, text) {
            var headEl = $root ? $root.find('#' + secHeadId(name))[0] : null;
            if (!headEl) return;
            var sumEl = headEl.querySelector('.vas_290_sc-sec__sum');
            if (sumEl) sumEl.textContent = text || '';
        }

        // ── Build the static DOM skeleton ─────────────────────────────────────
        function buildBodyHtml() {
            var wid = widgetID;
            var hd  = sectionState.header.data;
            var d   = hd || {};

            // Sections 2-7: built with buildSection helper after data loads.
            // Pre-build skeletons so content divs exist for renderSec to target.
            return (
                '<div id="vas_290_sc_identity_' + wid + '"></div>' +

                buildSection('customer',        'VAS_290_CustomerDetails',         'user',    '', skelLines(3)) +
                buildSection('contractDetails', 'VAS_290_ContractDetails',          'file',    '', skelLines(3)) +
                buildSection('productPricing',  'VAS_290_ProductAndPricing',        'cash',    '', skelLines(4)) +
                buildSection('billing',         'VAS_290_Billing',                  'cal',     '', skelLines(2)) +
                buildSection('renewal',         'VAS_290_RenewalAndCancellation',   'refresh', '', skelLines(2)) +
                buildSection('schedule',        'VAS_290_ContractSchedule',         'cash',    '', skelLines(3))
            );
        }

        // ── No-selection placeholder ──────────────────────────────────────────
        function renderNoSelectionState() {
            var bodyEl = $root ? $root.find('#vas_290_sc_body_' + widgetID)[0] : null;
            if (bodyEl) bodyEl.innerHTML = emptyState('VAS_290_NoContractSelected');
        }

        // ── Abort all pending XHR ─────────────────────────────────────────────
        function abortAll() {
            for (var sec in pendingXhr) {
                if (!pendingXhr.hasOwnProperty(sec)) continue;
                if (pendingXhr[sec] && pendingXhr[sec].readyState !== 4) {
                    try { pendingXhr[sec].abort(); } catch (e) { /* ignore */ }
                }
            }
            pendingXhr = {};
        }

        this.cleanup = function () {
            abortAll();
            currentCtrId = 0;
        };

        this.clear = function () {
            abortAll();
            currentCtrId = 0;
            schedPage    = 0;
            sectionState.header    = { loading: false, error: null, data: null, loaded: false };
            sectionState.schedules = { loading: false, error: null, data: null, loaded: false };
            if ($root) renderNoSelectionState();
        };

        // ── After header loads: render identity + sections 2-6 then fetch schedules ──
        function afterHeaderLoad(data) {
            renderHeaderSections(data);
            // Trigger schedule load immediately after header so billing section
            // can update its summary once schedules are also available
            fetchSection('schedules', 'GetContractSchedules', {}, null);
        }

        // ── Public: load a specific contract ─────────────────────────────────
        this.loadContract = function (contractId) {
            var newId = parseInt(contractId, 10) || 0;
            if (newId === currentCtrId) return;
            abortAll();
            currentCtrId = newId;
            schedPage    = 0;

            if (!newId) { renderNoSelectionState(); return; }

            // Restore section shells so callbacks have their target divs
            var $body = $root.find('#vas_290_sc_body_' + widgetID);
            $body.html(buildBodyHtml());
            wireSectionToggles();

            // Scroll to top on record change
            var bodyEl = $body[0];
            if (bodyEl) {
                bodyEl.scrollTop = 0;
                setTimeout(function () { if (bodyEl) bodyEl.scrollTop = 0; }, 0);
                setTimeout(function () { if (bodyEl) bodyEl.scrollTop = 0; }, 150);
            }

            fetchSection('header', 'GetContractOverview', {}, afterHeaderLoad);
        };

        // ── Initialize ────────────────────────────────────────────────────────
        this.Initialize = function () {
            widgetID = (this.widgetInfo && this.widgetInfo.AD_UserHomeWidgetID)
                ? this.widgetInfo.AD_UserHomeWidgetID
                : ($self.windowNo || 0);

            var wid = widgetID;

            $root = $(
                '<div class="vas_290_sc-shell" id="vas_290_sc_shell_' + wid + '">' +
                    '<div class="vas_290_sc-panel">' +
                        '<div class="vas_290_sc-body" id="vas_290_sc_body_' + wid + '" aria-live="polite" aria-busy="false">' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );

            // Delegated click: invoiced schedule rows → zoom to C_Invoice window.
            // Wired once on $root so it survives every re-render of the schedule grid.
            $root.on('click', '[data-open-invoice]', function (e) {
                e.stopPropagation();
                var invoiceId = +$(this).attr('data-open-invoice') || 0;
                var isSOTrx   = $(this).attr('data-sotrx') === 'Y';
                openInvoice(invoiceId, isSOTrx);
            });
            $root.on('keydown', '[data-open-invoice]', function (e) {
                if (e.which !== 13 && e.which !== 32) return;
                e.preventDefault();
                e.stopPropagation();
                var invoiceId = +$(this).attr('data-open-invoice') || 0;
                var isSOTrx   = $(this).attr('data-sotrx') === 'Y';
                openInvoice(invoiceId, isSOTrx);
            });

            // Initial empty state
            renderNoSelectionState();
        };

        this.getRoot = function () { return $root; };
    };

    // ── Prototype ─────────────────────────────────────────────────────────────

    /** Tab panel interface — called by the framework when the panel first opens */
    VAS.VAS_290_ServiceContractRightPanel.prototype.startPanel = function (windowNo, curTab) {
        this.windowNo = windowNo;
        this.curTab   = curTab;
        this.table_ID = curTab ? curTab.getAD_Table_ID() : 0;
        this.Initialize();
    };

    /** Tab panel interface — called on every record navigation */
    VAS.VAS_290_ServiceContractRightPanel.prototype.refreshPanelData = function (recordID, selectedRow) {
        if (selectedRow === undefined || recordID <= 0) { this.clear(); return; }
        this.record_ID   = recordID;
        this.selectedRow = selectedRow;
        this.loadContract(recordID);
    };

    /** Tab panel interface — called on panel resize */
    VAS.VAS_290_ServiceContractRightPanel.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /** Dashboard widget interface */
    VAS.VAS_290_ServiceContractRightPanel.prototype.widgetSizeChange = function (width) {
        this.panelWidth = width;
    };

    VAS.VAS_290_ServiceContractRightPanel.prototype.refreshWidget = function () { };

    /** Dashboard widget init */
    VAS.VAS_290_ServiceContractRightPanel.prototype.init = function (windowNo, frame) {
        this.frame      = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo   = windowNo;
        this.Initialize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_290_ServiceContractRightPanel.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) this.listener.widgetFirevalueChanged(value);
    };

    VAS.VAS_290_ServiceContractRightPanel.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_290_ServiceContractRightPanel.prototype.dispose = function () {
        if (typeof this.cleanup === 'function') this.cleanup();
        if (this.frame) this.frame.dispose();
        this.record_ID = this.table_ID = 0;
        this.curTab = this.frame = this.windowNo = null;
    };

})(VAS, jQuery);
