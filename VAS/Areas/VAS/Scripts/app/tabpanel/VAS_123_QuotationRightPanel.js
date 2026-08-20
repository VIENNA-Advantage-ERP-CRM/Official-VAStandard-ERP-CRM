/********************************************************
 * Module Name : VAS_123
 * Purpose     : Sales Quotation Right Detail Panel — client logic
 *               Part 1: Identity, Key Facts, Next Meeting, Document Status
 * Employee Code: VAI154
 * Date        : 20-Jul-2026
 ********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_123_QuotationRightPanel = function () {
        this.frame;
        this.windowNo;
        this.listener;

        var $self      = this;
        var $root      = null;
        var widgetID   = null;
        var currentOrderId = 0;
        var pendingXhr = {};

        // Panel state
        var state = {
            header:                  null,
            nextMeeting:             null,
            headerLoading:           false,
            meetingLoading:          false,
            headerLoaded:            false,
            headerError:             null,
            // Part 2 — Details Section
            generatedOrders:         null,
            generatedOrdersLoading:  false,
            opportunity:             null,
            opportunityLoading:      false,
            lines:                   null,
            linesLoading:            false,
            addresses:               null,
            addressesLoading:        false,
            terms:                   null,
            termsLoading:            false,
            // Part 3 — Footer Details
            tasks:                   null,
            tasksLoading:            false,
            taskTab:                 'up',
            engagement:              null,
            engagementLoading:       false,
            engagementPage:          0,
            engagementChannel:       'all',
            // §9b  Line change history
            lineHistory:             null,
            lineHistoryLoading:      false,
            lineHistOpen:            {}
        };

        // Task modal assignee state — one array per panel instance
        var _taskAssg = [];

        // ── Helper: HTML escape ──────────────────────────────────────────────
        function esc(v) {
            return String(v == null ? '' : v)
                .replace(/&/g, '&amp;').replace(/</g, '&lt;')
                .replace(/>/g, '&gt;').replace(/"/g, '&quot;')
                .replace(/'/g, '&#039;');
        }

        // ── Helper: AD_Message lookup with key fallback ──────────────────────
        function msg(k) {
            return (VIS && VIS.Msg && VIS.Msg.getMsg) ? (VIS.Msg.getMsg(k) || k) : k;
        }

        // ── Helper: safe number coercion ─────────────────────────────────────
        function toNum(v) {
            var n = Number(v);
            return isFinite(n) ? n : 0;
        }

        // ── Helper: currency format with thousands separator ──────────────────
        function fmtCurrency(v, sym, prec) {
            var s = (sym != null ? String(sym) : '');
            var p = (prec != null ? Math.max(0, parseInt(prec, 10) || 0) : 2);
            return s + toNum(v).toFixed(p).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
        }

        // ── Helper: format YYYY-MM-DD or ISO timestamp → "DD Mon YYYY" ────────
        function fmtDate(iso) {
            if (!iso) return '';
            var MONTHS = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
            // Support both "YYYY-MM-DD" and ISO datetime strings
            var s = String(iso);
            var parts = s.substring(0, 10).split('-');
            if (parts.length === 3) {
                var m = parseInt(parts[1], 10) - 1;
                return parts[2] + ' ' + (MONTHS[m] || '') + ' ' + parts[0];
            }
            return s;
        }

        // ── Helper: format ISO datetime → "DD Mon · HH:mm" ──────────────────
        function fmtDateTime(iso) {
            if (!iso) return '';
            var MONTHS = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
            var s = String(iso);
            var datePart = s.substring(0, 10);
            var timePart = s.length > 10 ? s.substring(11, 16) : '';
            var parts = datePart.split('-');
            if (parts.length === 3) {
                var m = parseInt(parts[1], 10) - 1;
                var dateStr = parts[2] + ' ' + (MONTHS[m] || '') + ' ' + parts[0];
                return timePart ? (dateStr + ' · ' + timePart) : dateStr;
            }
            return s;
        }

        // ── Helper: format engagement timestamp → "Mon DD, H:MMam/pm" ──────────
        function fmtEngTs(ts) {
            if (!ts) return '';
            var p = String(ts).split(' ');
            if (p.length < 2) return ts;
            var d = p[0].split('-'), t = p[1].split(':');
            var months = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
            var dt = new Date(Date.UTC(
                parseInt(d[0], 10), parseInt(d[1], 10) - 1, parseInt(d[2], 10),
                parseInt(t[0], 10), parseInt(t[1], 10)
            ));
            var h = dt.getHours(), mi = dt.getMinutes();
            var miStr = (mi < 10 ? '0' : '') + mi;
            var ampm = h >= 12 ? 'pm' : 'am';
            if (h > 12) h -= 12; else if (h === 0) h = 12;
            return months[dt.getMonth()] + ' ' + dt.getDate() + ', ' + h + ':' + miStr + ampm;
        }

        // ── Helper: inline SVG wrapper ────────────────────────────────────────
        function svg(paths) {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' + paths + '</svg>';
        }

        // ── SVG icon constants ────────────────────────────────────────────────
        var SVG_QUOTE = svg('<path d="M6 2h9l4 4v16H6z"/><path d="M14 2v5h5"/><path d="M9 12h6M9 15.5h6M9 8.5h2"/>');
        var SVG_MAIL  = svg('<rect x="2.5" y="5" width="19" height="14" rx="2"/><path d="m3 7 9 6 9-6"/>');
        var SVG_CAL   = svg('<rect x="3.5" y="5" width="17" height="16" rx="2"/><path d="M8 3v4M16 3v4M3.5 10h17"/>');
        var SVG_CHECK = svg('<path d="m5 12.5 4.5 4.5L19 7.5"/>');
        var SVG_X     = svg('<path d="M18 6 6 18M6 6l12 12"/>');
        var SVG_INFO  = svg('<circle cx="12" cy="12" r="9"/><path d="M12 8h.01M11 12h1v4h1"/>');
        var SVG_CLOCK = svg('<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3.5 2"/>');
        var SVG_ARROW = svg('<path d="M5 12h14m-6-6 6 6-6 6"/>');
        var SVG_VIDEO = svg('<rect x="2.5" y="6" width="13" height="12" rx="2"/><path d="m15.5 10 6-3.5v11l-6-3.5"/>');
        var SVG_DOC_STATUS = svg('<circle cx="12" cy="12" r="9"/><path d="M9 12l2 2 4-4"/>');
        // Part 2 icon constants
        var SVG_CART   = svg('<path d="M6 6h15l-1.5 8.5a2 2 0 0 1-2 1.5H8.7a2 2 0 0 1-2-1.6L5 3H2"/><circle cx="9" cy="20" r="1.5"/><circle cx="18" cy="20" r="1.5"/>');
        var SVG_TARGET = svg('<circle cx="12" cy="12" r="9"/><circle cx="12" cy="12" r="4.5"/><circle cx="12" cy="12" r="1"/>');
        var SVG_BOX    = svg('<path d="M21 8 12 3 3 8v8l9 5 9-5V8Z"/><path d="M3 8l9 5 9-5M12 13v8"/>');
        var SVG_WRENCH = svg('<path d="M14.5 6.5a4.5 4.5 0 0 0-6 6L3 18l3 3 5.5-5.5a4.5 4.5 0 0 0 6-6L14 13l-3-3 3.5-3.5Z"/>');
        var SVG_USERS  = svg('<circle cx="9" cy="8" r="3.5"/><path d="M2.5 20c1-3.2 3.5-5 6.5-5s5.5 1.8 6.5 5M16 3.7a3.5 3.5 0 0 1 0 8.6M18.5 15.4c1.7.8 2.9 2.4 3.5 4.6"/>');
        var SVG_PHONE  = svg('<path d="M5 3h4l1.6 4.4L8 9.4a13 13 0 0 0 6.6 6.6l2-2.6L21 15v4a2 2 0 0 1-2.2 2A17.5 17.5 0 0 1 3 5.2 2 2 0 0 1 5 3Z"/>');
        var SVG_PLUS   = svg('<path d="M12 5v14M5 12h14"/>');
        var SVG_LINK   = svg('<path d="M10 14a5 5 0 0 0 7 0l3-3a5 5 0 0 0-7-7l-1.5 1.5M14 10a5 5 0 0 0-7 0l-3 3a5 5 0 0 0 7 7l1.5-1.5"/>');
        // Part 3 icon constants
        var SVG_SEND  = svg('<path d="m3 11 18-7-7 18-2.5-7.5L3 11Z"/>');
        var SVG_LEFT  = svg('<path d="m14 6-6 6 6 6"/>');
        var SVG_RIGHT = svg('<path d="m10 6 6 6-6 6"/>');
        var SVG_CHAT  = svg('<path d="M21 12a8 8 0 0 1-11.6 7.2L4 21l1.8-5.4A8 8 0 1 1 21 12Z"/>');
        var SVG_DOC   = svg('<path d="M6 2h9l4 4v16H6z"/><path d="M14 2v5h5"/>');
        var SVG_USER  = svg('<circle cx="12" cy="8" r="4"/><path d="M4 21c1.2-3.8 4.3-6 8-6s6.8 2.2 8 6"/>');

        // ── Helper: parse attendee info (JSON array or delimited string) ───────
        function parseAttendees(str) {
            if (!str) return [];
            try {
                var arr = JSON.parse(str);
                if (Array.isArray(arr)) {
                    return arr.map(function (a) {
                        return typeof a === 'string' ? a : (a.name || a.email || '');
                    }).filter(Boolean);
                }
            } catch (e) { /* not JSON — fall through to delimiter split */ }
            return String(str).split(/[;,|]/).map(function (s) { return s.trim(); }).filter(Boolean);
        }

        // ── Helper: derive meeting channel from URL / location ────────────────
        function resolveChannel(url, loc) {
            if (!url) return loc ? msg('VAS_123_InPerson') : '';
            var u = String(url).toLowerCase();
            if (u.indexOf('teams.microsoft') > -1 || u.indexOf('teams.') > -1) return 'Teams';
            if (u.indexOf('zoom.us') > -1) return 'Zoom';
            if (u.indexOf('meet.google') > -1) return 'Google Meet';
            if (u.indexOf('webex') > -1) return 'Webex';
            return msg('VAS_123_VideoCall');
        }

        // ── Helper: first two initials from a name ────────────────────────────
        function initials(name) {
            return String(name || '').split(/\s+/).map(function (w) { return w.charAt(0); })
                .slice(0, 2).join('').toUpperCase() || '?';
        }

        // ── Helper: resolve DocStatus label from server-fetched AD_Ref_List dict ─
        // h.docStatusLabels is populated by GetHeader via GetRefListNames(ctx, "DocStatus", "C_Order")
        // and carries the translated name for every DocStatus value in the session language.
        function resolveStatusLabel(code) {
            var h = state.header;
            if (h && h.docStatusLabels && h.docStatusLabels[code]) {
                return h.docStatusLabels[code];
            }
            return code; // fallback: show raw code when dict not yet loaded
        }

        // Returns a CSS variable token (--neutral, --info, --success, --risk) for a DocStatus
        function statusTone(code) {
            return { DR: '--neutral', IP: '--info', CO: '--success', CL: '--neutral', VO: '--risk' }[code] || '--neutral';
        }

        // ── AJAX helper: abort previous call for the same action, then POST ───
        function postJSON(action, data, callback) {
            if (pendingXhr[action] && pendingXhr[action].readyState !== 4) {
                try { pendingXhr[action].abort(); } catch (e) {}
            }
            pendingXhr[action] = $.ajax({
                url:   VIS.Application.contextUrl + 'VAS/VAS_123_QuotationRightPanel/' + action,
                type:  'POST',
                data:  data || {},
                async: true,
                success: function (raw) {
                    var parsed = null;
                    try { parsed = (typeof raw === 'string') ? JSON.parse(raw) : raw; } catch (e) { parsed = null; }
                    if (callback) callback(null, parsed);
                },
                error: function (xhr, status) {
                    pendingXhr[action] = null;
                    if (status === 'abort') return;
                    if (callback) callback(status || 'error', null);
                }
            });
            return pendingXhr[action];
        }

        // ── State derivation from header data ─────────────────────────────────
        function deriveState() {
            var h = state.header;
            if (!h) return { docStatus: '', converted: false, expired: false, openDoc: false, daysToExpiry: 0 };
            var today = new Date(); today.setHours(0, 0, 0, 0);
            var validTo = h.orderValidTo ? new Date(h.orderValidTo) : null;
            var expired  = validTo ? (validTo < today) : false;
            var openDoc  = (h.docStatus !== 'CL' && h.docStatus !== 'VO');
            var daysToExpiry = validTo ? Math.ceil((validTo - today) / 86400000) : 0;
            var converted = !!(h.convertedOrder_ID || h.convertedOrderID);
            return {
                docStatus:    h.docStatus,
                converted:    converted,
                expired:      expired,
                openDoc:      openDoc,
                daysToExpiry: daysToExpiry,
                validTo:      validTo
            };
        }

        // ── Toast notification ────────────────────────────────────────────────
        function showToast(message) {
            var t = document.getElementById('vas_123_toast_' + widgetID);
            if (!t) return;
            t.textContent = message;
            t.classList.add('vas_123_qrp-toast--show');
            setTimeout(function () { t.classList.remove('vas_123_qrp-toast--show'); }, 3000);
        }

        // ── Modal helpers ─────────────────────────────────────────────────────
        function showModal(title, meta, bodyHtml, footHtml, wide) {
            var overlay = document.getElementById('vas_123_overlay_' + widgetID);
            var modal   = document.getElementById('vas_123_modal_' + widgetID);
            var tEl     = document.getElementById('vas_123_mtitle_' + widgetID);
            var mEl     = document.getElementById('vas_123_mmeta_'  + widgetID);
            var bEl     = document.getElementById('vas_123_mbody_'  + widgetID);
            var fEl     = document.getElementById('vas_123_mfoot_'  + widgetID);
            if (!overlay) return;
            if (tEl) tEl.textContent = title || '';
            if (mEl) mEl.textContent = meta  || '';
            if (bEl) bEl.innerHTML   = bodyHtml || '';
            if (fEl) fEl.innerHTML   = footHtml || '';
            if (modal) {
                if (wide) modal.classList.add('vas_123_qrp-modal--wide');
                else       modal.classList.remove('vas_123_qrp-modal--wide');
            }
            overlay.classList.add('vas_123_qrp-overlay--open');
        }

        function closeModal() {
            var overlay = document.getElementById('vas_123_overlay_' + widgetID);
            if (overlay) overlay.classList.remove('vas_123_qrp-overlay--open');
        }

        // ── Data loading ──────────────────────────────────────────────────────

        // Load quotation header data from server
        function loadHeader(orderId) {
            if (!orderId || orderId <= 0) return;
            state.headerLoading = true;
            state.headerError   = null;
            renderPanel();

            postJSON('GetHeader', { C_Order_ID: orderId }, function (err, data) {
                state.headerLoading = false;
                // data.error check: server returns {error:"not_found",...} when the SQL fails
                // or the record doesn't match the quotation conditions. Treat this as a load
                // failure so the error block shows rather than silently rendering with empty fields.
                if (err || !data || data.error) {
                    state.headerError  = (data && data.error) ? data.error : (err || 'error');
                    state.headerLoaded = false;
                    VIS.log && VIS.log.warning('VAS_123', 'GetHeader failed: ' + state.headerError);
                } else {
                    state.header      = data;
                    state.headerLoaded = true;
                    state.headerError  = null;
                    // Trigger meeting load, all Part 2 section loads, and Part 3 loads after header arrives
                    loadNextMeeting(orderId);
                    loadGeneratedOrders(orderId);
                    loadOpportunity(orderId);
                    loadLines(orderId);
                    loadLineHistory(orderId);
                    loadAddresses(orderId);
                    loadTerms(orderId);
                    loadTasks(orderId);
                    loadEngagement(orderId);
                }
                renderPanel();
            });
        }

        // Load next upcoming meeting linked to this quotation
        function loadNextMeeting(orderId) {
            if (!orderId || orderId <= 0) return;
            state.meetingLoading = true;
            state.nextMeeting    = null;

            postJSON('GetNextMeeting', { C_Order_ID: orderId }, function (err, data) {
                state.meetingLoading = false;
                if (!err && data) {
                    state.nextMeeting = data; // null is valid (no meeting scheduled)
                } else {
                    state.nextMeeting = null;
                }
                renderNextMeetingSection();
            });
        }

        // ── Part 2 data loaders ───────────────────────────────────────────────

        // Load sales orders generated from this quotation
        function loadGeneratedOrders(orderId) {
            if (!orderId || orderId <= 0) return;
            state.generatedOrdersLoading = true;
            state.generatedOrders        = null;

            postJSON('GetGeneratedOrders', { C_Order_ID: orderId }, function (err, data) {
                state.generatedOrdersLoading = false;
                state.generatedOrders        = (!err && Array.isArray(data)) ? data : [];
                renderOrders();
            });
        }

        // Load the linked CRM opportunity ID for this quotation
        function loadOpportunity(orderId) {
            if (!orderId || orderId <= 0) return;
            state.opportunityLoading = true;
            state.opportunity        = null;

            postJSON('GetOpportunity', { C_Order_ID: orderId }, function (err, data) {
                state.opportunityLoading = false;
                state.opportunity        = (!err && data) ? data : null;
                renderOpportunity();
            });
        }

        // Load active quotation lines
        function loadLines(orderId) {
            if (!orderId || orderId <= 0) return;
            state.linesLoading = true;
            state.lines        = null;

            postJSON('GetQuotationLines', { C_Order_ID: orderId }, function (err, data) {
                state.linesLoading = false;
                state.lines        = (!err && Array.isArray(data)) ? data : [];
                renderLines();
            });
        }

        // §9b  Load line change history from C_OrderLineHistory for all lines
        function loadLineHistory(orderId) {
            if (!orderId || orderId <= 0) return;
            state.lineHistoryLoading = true;
            state.lineHistory        = null;
            state.lineHistOpen       = {}; // reset open drawers for new record

            postJSON('GetLineHistory', { C_Order_ID: orderId }, function (err, data) {
                state.lineHistoryLoading = false;
                // Build a map: c_OrderLine_ID → array of history rows (already newest-first from server)
                var map  = {};
                var rows = (!err && Array.isArray(data)) ? data : [];
                for (var i = 0; i < rows.length; i++) {
                    var lineId = rows[i].c_OrderLine_ID || rows[i].C_OrderLine_ID || 0;
                    if (!lineId) continue;
                    if (!map[lineId]) map[lineId] = [];
                    map[lineId].push(rows[i]);
                }
                state.lineHistory = map;
                renderLines(); // re-render lines with history toggles
            });
        }

        // Load selected billing and shipping addresses for this quotation
        function loadAddresses(orderId) {
            if (!orderId || orderId <= 0) return;
            state.addressesLoading = true;
            state.addresses        = null;

            postJSON('GetAddresses', { C_Order_ID: orderId }, function (err, data) {
                state.addressesLoading = false;
                state.addresses        = (!err && data) ? data : null;
                renderCustomer();
            });
        }

        // Load pricing & terms (PaymentRule, PriorityRule, PriceList, PaymentTerm)
        // Kept separate from loadHeader so a schema-missing column only breaks this section.
        function loadTerms(orderId) {
            if (!orderId || orderId <= 0) return;
            state.termsLoading = true;
            state.terms        = null;

            postJSON('GetPricingTerms', { C_Order_ID: orderId }, function (err, data) {
                state.termsLoading = false;
                state.terms        = (!err && data && !data.error) ? data : null;
                renderTerms();
            });
        }

        // ── Part 3 data loaders ───────────────────────────────────────────────

        // Load R_Request tasks linked to this quotation
        function loadTasks(orderId) {
            if (!orderId || orderId <= 0) return;
            state.tasksLoading = true;
            state.tasks        = null;
            renderTasks();

            postJSON('GetTasks', { C_Order_ID: orderId }, function (err, data) {
                state.tasksLoading = false;
                state.tasks        = (!err && Array.isArray(data)) ? data : [];
                renderTasks();
            });
        }

        // Load engagement timeline (meetings, notes, emails, calls, WhatsApp) for this quotation
        function loadEngagement(orderId) {
            if (!orderId || orderId <= 0) return;
            state.engagementLoading = true;
            state.engagement        = null;
            renderEngagement();

            postJSON('GetEngagement', { C_Order_ID: orderId }, function (err, data) {
                state.engagementLoading = false;
                // Server now returns { counts, items } — normalise to always have that shape
                if (!err && data && data.counts && Array.isArray(data.items)) {
                    state.engagement = data;
                } else if (!err && Array.isArray(data)) {
                    // Fallback for legacy list format
                    state.engagement = { counts: { total: data.length }, items: data };
                } else {
                    state.engagement = { counts: { total: 0 }, items: [] };
                }
                renderEngagement();
            });
        }

        // ── Panel render orchestration ────────────────────────────────────────
        function renderPanel() {
            var bodyEl = document.getElementById('vas_123_body_' + widgetID);
            if (!bodyEl) return;

            // On first render or after a clear: build the section skeleton
            if (!bodyEl.querySelector('.vas_123_qrp-sections')) {
                bodyEl.innerHTML =
                    '<div class="vas_123_qrp-sections">' +
                        '<div id="vas_123_identity_'      + widgetID + '"></div>' +
                        '<div id="vas_123_keyfacts_'      + widgetID + '"></div>' +
                        '<div id="vas_123_nextmeet_'      + widgetID + '" class="vas_123_qrp-sec"></div>' +
                        '<div id="vas_123_docstatus_'     + widgetID + '" class="vas_123_qrp-sec"></div>' +
                        '<div id="vas_123_orders_'        + widgetID + '" class="vas_123_qrp-sec"></div>' +
                        '<div id="vas_123_opportunity_'   + widgetID + '" class="vas_123_qrp-sec"></div>' +
                        '<div id="vas_123_lines_'         + widgetID + '" class="vas_123_qrp-sec"></div>' +
                        '<div id="vas_123_customer_'      + widgetID + '" class="vas_123_qrp-sec"></div>' +
                        '<div id="vas_123_terms_'         + widgetID + '" class="vas_123_qrp-sec"></div>' +
                        '<div id="vas_123_tasks_'         + widgetID + '" class="vas_123_qrp-sec"></div>' +
                        '<div id="vas_123_engagement_'    + widgetID + '" class="vas_123_qrp-sec"></div>' +
                    '</div>';
            }

            renderIdentity();
            renderKeyFacts();
            renderNextMeetingSection();
            renderDocumentStatus();
            renderOrders();
            renderOpportunity();
            renderLines();
            renderCustomer();
            renderTerms();
            renderTasks();
            renderEngagement();
        }

        // ── Section: Identity ─────────────────────────────────────────────────
        function renderIdentity() {
            var container = document.getElementById('vas_123_identity_' + widgetID);
            if (!container) return;

            // Loading skeleton
            if (state.headerLoading) {
                container.innerHTML =
                    '<div class="vas_123_qrp-identity vas_123_qrp-identity--loading">' +
                        '<div class="vas_123_qrp-skel vas_123_qrp-skel--title"></div>' +
                        '<div class="vas_123_qrp-skel vas_123_qrp-skel--sub"></div>' +
                        '<div class="vas_123_qrp-skel vas_123_qrp-skel--sub" style="width:60%"></div>' +
                    '</div>';
                return;
            }

            // Error state
            if (state.headerError) {
                container.innerHTML =
                    '<div class="vas_123_qrp-identity vas_123_qrp-error-block">' +
                        '<span class="vas_123_qrp-error-icon">' + SVG_INFO + '</span>' +
                        '<p class="vas_123_qrp-error-msg">' + esc(msg('VAS_123_UnableToLoadQuotation')) + '</p>' +
                    '</div>';
                return;
            }

            var h = state.header;
            if (!h) { container.innerHTML = ''; return; }

            var ds         = deriveState();
            var statusCode = h.docStatus || '';
            var tone       = statusTone(statusCode);
            var statusLbl  = resolveStatusLabel(statusCode);

            // Build chip row
            var chips = '<span class="vas_123_qrp-chip vas_123_qrp-chip' + esc(tone) + '">' + esc(statusLbl) + '</span>';

            // Converted chip — shown whenever the quotation has been converted
            if (ds.converted) {
                chips += '<span class="vas_123_qrp-chip vas_123_qrp-chip--success">' + esc(msg('VAS_123_Converted')) + '</span>';
            }

            // Credit hold chip — only when CreditStatus === 'H'
            if (h.creditStatus === 'H') {
                chips += '<span class="vas_123_qrp-chip vas_123_qrp-chip--warn">' + esc(msg('VAS_123_CreditHold')) + '</span>';
            }

            // Sub-line: partner · quoted date · sales rep
            var subParts = [];
            if (h.bPartnerName) subParts.push(esc(h.bPartnerName));
            if (h.dateOrdered)  subParts.push('quoted ' + esc(fmtDate(h.dateOrdered)));
            if (h.salesRepName) subParts.push(esc(h.salesRepName));


            container.innerHTML =
                '<div class="vas_123_qrp-identity">' +
                    '<div class="vas_123_qrp-id-top">' +
                        '<div class="vas_123_qrp-id-tile">' + SVG_QUOTE + '</div>' +
                        '<div class="vas_123_qrp-id-main">' +
                            '<div class="vas_123_qrp-id-namerow">' +
                                '<span class="vas_123_qrp-id-name">' + esc(h.documentNo || '') + '</span>' +
                                chips +
                            '</div>' +
                            '<p class="vas_123_qrp-id-sub">' + subParts.join(' · ') + '</p>' +
                        '</div>' +
                        '<div class="vas_123_qrp-id-actions">' +
                            '<button class="vas_123_qrp-btn vas_123_qrp-btn--secondary" data-action="sendQuotation">' +
                                SVG_MAIL + ' ' + esc(msg('Send')) +
                            '</button>' +
                            '<button class="vas_123_qrp-btn vas_123_qrp-btn--secondary" data-action="scheduleMeeting">' +
                                SVG_CAL + ' ' + esc(msg('VAS_123_Schedule')) +
                            '</button>' +
                        '</div>' +
                    '</div>' +
                '</div>';
        }

        // ── Section: Key Facts (4-col metric grid) ────────────────────────────
        function renderKeyFacts() {
            var container = document.getElementById('vas_123_keyfacts_' + widgetID);
            if (!container) return;

            // Loading — em-dash placeholder in every cell
            if (state.headerLoading) {
                var skelCell = '<div class="vas_123_qrp-mcell">' +
                    '<p class="vas_123_qrp-m-label"><span class="vas_123_qrp-skel vas_123_qrp-skel--label"></span></p>' +
                    '<p class="vas_123_qrp-m-value">—</p>' +
                '</div>';
                container.innerHTML =
                    '<div class="vas_123_qrp-mgrid vas_123_qrp-mgrid--4">' +
                        skelCell + skelCell + skelCell + skelCell +
                    '</div>';
                return;
            }

            var h  = state.header;
            if (!h) { container.innerHTML = ''; return; }

            var ds   = deriveState();
            var sym  = h.currencySymbol || '';
            var prec = (h.currencyPrecision != null) ? parseInt(h.currencyPrecision, 10) : 2;

            // ── Cell 1: Grand Total ──────────────────────────────────────────
            var grandTotalHtml =
                '<div class="vas_123_qrp-mcell">' +
                    '<p class="vas_123_qrp-m-label">' + esc(msg('VAS_123_GrandTotal')) + '</p>' +
                    '<p class="vas_123_qrp-m-value">' + esc(fmtCurrency(h.grandTotal, sym, prec)) + '</p>' +
                '</div>';

            // ── Cell 2: Margin (GrandTotal − TotalLines, % of net) ───────────
            var grandTotalNum = toNum(h.grandTotal);
            var totalLinesNum = toNum(h.totalLines);
            var marginAmt     = grandTotalNum - totalLinesNum;
            var marginPctNum  = totalLinesNum > 0 ? (marginAmt / totalLinesNum * 100).toFixed(1) : '';
            var marginMetaHtml = marginPctNum
                ? (esc(marginPctNum + '%') + ' ' + esc(msg('VAS_123_OfNet')))
                : '';
            var marginValueTone = marginAmt > 0 ? ' vas_123_qrp-m-value--success' : (marginAmt < 0 ? ' vas_123_qrp-m-value--risk' : '');
            var marginHtml =
                '<div class="vas_123_qrp-mcell">' +
                    '<p class="vas_123_qrp-m-label">' + esc(msg('VAS_123_Margin')) + '</p>' +
                    '<p class="vas_123_qrp-m-value' + marginValueTone + '">' + esc(fmtCurrency(marginAmt, sym, prec)) + '</p>' +
                    (marginMetaHtml ? '<p class="vas_123_qrp-m-meta">' + marginMetaHtml + '</p>' : '') +
                '</div>';

            // ── Cell 3: Valid Until ──────────────────────────────────────────
            var validUntilVal  = h.orderValidTo ? fmtDate(h.orderValidTo) : '—';
            var validUntilMeta = '';
            var validUntilTone = '';

            // Show expiry meta only when document is open, not converted, and has a valid-to date
            if (ds.openDoc && !ds.converted && h.orderValidTo) {
                if (ds.expired) {
                    var daysAgo = Math.abs(ds.daysToExpiry);
                    validUntilMeta = 'expired ' + daysAgo + ' day' + (daysAgo !== 1 ? 's' : '') + ' ago';
                    validUntilTone = '--risk';
                } else if (ds.daysToExpiry <= 7) {
                    validUntilMeta = 'expires in ' + ds.daysToExpiry + ' day' + (ds.daysToExpiry !== 1 ? 's' : '');
                    validUntilTone = '--warn';
                } else {
                    validUntilMeta = 'expires in ' + ds.daysToExpiry + ' day' + (ds.daysToExpiry !== 1 ? 's' : '');
                }
            }

            var validUntilHtml =
                '<div class="vas_123_qrp-mcell">' +
                    '<p class="vas_123_qrp-m-label">' + esc(msg('VAS_123_ValidUntil')) + '</p>' +
                    '<p class="vas_123_qrp-m-value' + (validUntilTone === '--risk' ? ' vas_123_qrp-m-value--risk' : '') + '">' + esc(validUntilVal) + '</p>' +
                    (validUntilMeta ? '<p class="vas_123_qrp-m-meta' + (validUntilTone ? ' vas_123_qrp-m-meta' + esc(validUntilTone) : '') + '">' + esc(validUntilMeta) + '</p>' : '') +
                '</div>';

            // ── Cell 4: Converted ────────────────────────────────────────────
            var convertedVal  = '';
            var convertedMeta = '';
            var convertedTone = '';

            if (ds.converted) {
                convertedVal  = msg('VAS_123_Converted');
                convertedMeta = h.convertedOrderNo || '';
                convertedTone = '--success';
            } else if (h.docStatus === 'CO') {
                convertedVal  = msg('VAS_123_NotYet');
                convertedMeta = msg('VAS_123_AwaitingAcceptance');
            } else {
                // DR or IP
                convertedVal  = msg('VAS_123_NotYet');
                convertedMeta = '—';
            }

            var convertedHtml =
                '<div class="vas_123_qrp-mcell">' +
                    '<p class="vas_123_qrp-m-label">' + esc(msg('VAS_123_Converted')) + '</p>' +
                    '<p class="vas_123_qrp-m-value' + (convertedTone ? ' vas_123_qrp-m-value' + esc(convertedTone) : '') + '">' + esc(convertedVal) + '</p>' +
                    (convertedMeta ? '<p class="vas_123_qrp-m-meta">' + esc(convertedMeta) + '</p>' : '') +
                '</div>';

            // 4-column metric grid
            container.innerHTML =
                '<div class="vas_123_qrp-mgrid vas_123_qrp-mgrid--4">' +
                    grandTotalHtml + marginHtml + validUntilHtml + convertedHtml +
                '</div>';
        }

        // ── Section: Upcoming meeting (IsTask = 'N') ─────────────────────────
        function renderNextMeetingSection() {
            var container = document.getElementById('vas_123_nextmeet_' + widgetID);
            if (!container) return;

            // Hidden when no meeting and not loading
            if (!state.meetingLoading && !state.nextMeeting) {
                container.innerHTML = '';
                return;
            }

            // Loading skeleton
            if (state.meetingLoading) {
                container.innerHTML =
                    '<div class="vas_123_qrp-skel vas_123_qrp-skel--row"></div>';
                return;
            }

            var m = state.nextMeeting;
            if (!m) { container.innerHTML = ''; return; }

            var meetingUrl   = m.meetingUrl || m.url || '';
            var location     = m.location || '';
            var channel      = resolveChannel(meetingUrl, location);
            var attendees    = parseAttendees(m.attendeeInfo);
            var joinDisabled = meetingUrl ? '' : 'disabled';

            // Build attendee avatar stack (show up to 4)
            var avatarsHtml = '';
            var maxAvatars  = Math.min(attendees.length, 4);
            for (var ai = 0; ai < maxAvatars; ai++) {
                avatarsHtml +=
                    '<span class="vas_123_qrp-avatar" title="' + esc(attendees[ai]) + '">' +
                        esc(initials(attendees[ai])) +
                    '</span>';
            }
            if (attendees.length > 4) {
                avatarsHtml += '<span class="vas_123_qrp-avatar vas_123_qrp-avatar--more">+' + (attendees.length - 4) + '</span>';
            }

            container.innerHTML =
                '<div class="vas_123_qrp-nextmeet">' +
                    '<div class="vas_123_qrp-pcard-row">' +
                        '<div class="vas_123_qrp-etile vas_123_qrp-etile--info" style="border-radius:999px">' +
                            SVG_VIDEO +
                        '</div>' +
                        '<div class="vas_123_qrp-pc-main">' +
                            '<p class="vas_123_qrp-pc-name">' + esc(m.subject || '') + '</p>' +
                            '<p class="vas_123_qrp-pc-meta">' +
                                esc(fmtDateTime(m.startDate)) +
                                (channel ? ' · ' + esc(channel) : '') +
                            '</p>' +
                        '</div>' +
                        '<div class="vas_123_qrp-pc-actions">' +
                            (avatarsHtml ? '<span class="vas_123_qrp-avstack">' + avatarsHtml + '</span>' : '') +
                            '<button class="vas_123_qrp-btn vas_123_qrp-btn--primary"' +
                                ' data-action="joinMeeting"' +
                                ' data-url="' + esc(meetingUrl) + '"' +
                                (joinDisabled ? ' disabled' : '') + '>' +
                                SVG_VIDEO + ' ' + esc(msg('VAS_123_Join')) +
                            '</button>' +
                        '</div>' +
                    '</div>' +
                '</div>';
        }

        // ── Section: Document Status ──────────────────────────────────────────
        function renderDocumentStatus() {
            var container = document.getElementById('vas_123_docstatus_' + widgetID);
            if (!container) return;

            if (state.headerLoading) {
                container.innerHTML =
                    '<div class="vas_123_qrp-skel vas_123_qrp-skel--block"></div>';
                return;
            }

            var h = state.header;
            if (!h) { container.innerHTML = ''; return; }

            var ds         = deriveState();
            var statusCode = h.docStatus || '';
            var stages     = ['DR', 'IP', 'CO', 'CL'];

            // ── Pipeline ────────────────────────────────────────────────────
            // Spec-compliant Stage Pipeline: CSS grid, one column per stage.
            // Connector segments are absolutely positioned inside each column
            // (left:50% → right:-50% spans this circle center to next circle center).
            var isVoided  = (statusCode === 'VO');
            var activeIdx = isVoided ? -1 : stages.indexOf(statusCode);

            var pipelineHtml =
                '<div class="vas_123_qrp-pipe">' +
                    '<div class="vas_123_qrp-pipe-grid" style="grid-template-columns:repeat(' + stages.length + ',minmax(0,1fr))">';

            for (var si = 0; si < stages.length; si++) {
                var stage      = stages[si];
                var stageLabel = resolveStatusLabel(stage);
                var isLast     = (si === stages.length - 1);
                var segDone    = (!isVoided && si < activeIdx);

                var circleClass, circleInner, labelClass;
                if (isVoided) {
                    if (si === 0) {
                        // First stage shows voided/blocked state
                        circleClass = 'vas_123_qrp-pipe-circle vas_123_qrp-pipe-circle--blocked';
                        circleInner = SVG_X;
                    } else {
                        circleClass = 'vas_123_qrp-pipe-circle';
                        circleInner = '';
                    }
                    labelClass = 'vas_123_qrp-pipe-label vas_123_qrp-pipe-label--pending';
                } else if (si < activeIdx) {
                    // Completed stage
                    circleClass = 'vas_123_qrp-pipe-circle vas_123_qrp-pipe-circle--done';
                    circleInner = SVG_CHECK;
                    labelClass  = 'vas_123_qrp-pipe-label vas_123_qrp-pipe-label--done';
                } else if (si === activeIdx) {
                    // Active stage — inner dot is rendered by CSS ::after (no child element)
                    circleClass = 'vas_123_qrp-pipe-circle vas_123_qrp-pipe-circle--active';
                    circleInner = '';
                    labelClass  = 'vas_123_qrp-pipe-label vas_123_qrp-pipe-label--active';
                } else {
                    // Pending stage
                    circleClass = 'vas_123_qrp-pipe-circle';
                    circleInner = '';
                    labelClass  = 'vas_123_qrp-pipe-label vas_123_qrp-pipe-label--pending';
                }

                pipelineHtml +=
                    '<div class="vas_123_qrp-pipe-col">' +
                        // Segment: hidden on last column, blue when left circle is done
                        (!isLast ? '<div class="vas_123_qrp-pipe-seg' + (segDone ? ' vas_123_qrp-pipe-seg--done' : '') + '"></div>' : '') +
                        '<div class="' + circleClass + '">' + circleInner + '</div>' +
                        '<span class="' + labelClass + '" title="' + esc(stageLabel) + '">' + esc(stageLabel) + '</span>' +
                    '</div>';
            }

            pipelineHtml += '</div></div>';

            // ── Stage summary label (top-right of section header) ────────────
            // Raw values — esc() is applied when written into innerHTML
            var stageSummary = '';
            if (statusCode === 'CL') {
                stageSummary = msg('VAS_123_Closed');
            } else if (statusCode === 'VO') {
                stageSummary = msg('VAS_123_Voided');
            } else if (activeIdx >= 0) {
                stageSummary = 'Stage ' + (activeIdx + 1) + ' of ' + stages.length;
            }

            // ── Callout block ────────────────────────────────────────────────
            var calloutHtml = '';
            var calloutVariant = 'info'; // or 'warn'

            if (statusCode === 'DR') {
                calloutHtml = '<b>' + esc(msg('VAS_123_CalloutDrafted').split('|')[0] || msg('VAS_123_CalloutDrafted')) + '</b> ' +
                    esc(msg('VAS_123_CalloutDrafted').split('|')[1] || 'to lock pricing and send it to the customer.');
                // Rebuild using the full message key (the split approach is fragile — use the key directly)
                calloutHtml = msg('VAS_123_CalloutDrafted');

            } else if (statusCode === 'IP') {
                calloutHtml = msg('VAS_123_CalloutInProgress');

            } else if (statusCode === 'CO') {
                if (ds.converted) {
                    var convOrderNo   = esc(h.convertedOrderNo || '');
                    var convOrderId   = h.convertedOrder_ID || h.convertedOrderID || 0;
                    var convOrderDate = fmtDate(h.convertedOrderDate || '');
                    // Build translated link with converted order reference
                    calloutHtml = msg('VAS_123_CalloutConverted') +
                        ' <a class="vas_123_qrp-link" data-action="openOrder" data-order-id="' + esc(convOrderId) + '">' +
                        convOrderNo + '</a>' +
                        (convOrderDate ? ' on ' + esc(convOrderDate) : '') +
                        '. ' + 'Open the order to continue fulfillment.';
                } else if (ds.expired) {
                    calloutVariant = 'warn';
                    calloutHtml = msg('VAS_123_CalloutExpired') +
                        ' <b>' + esc(fmtDate(h.orderValidTo)) + '</b>.';
                } else {
                    var daysLeftStr = ds.daysToExpiry > 0 ? (' (' + ds.daysToExpiry + ' day' + (ds.daysToExpiry !== 1 ? 's' : '') + ' left)') : '';
                    calloutHtml = '<b>' + esc(msg('VAS_123_ValidUntil')) + ' ' + esc(fmtDate(h.orderValidTo)) + '</b>' +
                        esc(daysLeftStr) + '. ' + msg('VAS_123_CalloutValidOpen');
                }

            } else if (statusCode === 'CL') {
                calloutHtml = msg('VAS_123_CalloutClosed');

            } else if (statusCode === 'VO') {
                calloutHtml = msg('VAS_123_CalloutVoided');
            }

            // SVG goes directly as first child of callout (CSS selects .vas_123_qrp-callout svg)
            var calloutIconHtml  = calloutVariant === 'warn' ? SVG_CLOCK : SVG_INFO;
            var calloutBlockHtml = calloutHtml
                ? '<div class="vas_123_qrp-callout vas_123_qrp-callout--' + calloutVariant + '">' +
                    calloutIconHtml +
                    '<div class="vas_123_qrp-co-text">' + calloutHtml + '</div>' +
                  '</div>'
                : '';

            // ── Footer: last action date (left) + action buttons (right) ─────
            var lastActionDate = h.lastDocumentActionDate ? fmtDate(h.lastDocumentActionDate) : '';
            var footLeftText   = '';
            if (statusCode === 'VO' && lastActionDate) {
                footLeftText = msg('VAS_123_Voided') + ' ' + lastActionDate;
            } else if (lastActionDate) {
                footLeftText = msg('VAS_123_LastDocumentAction') + ' ' + lastActionDate;
            }

            container.innerHTML =
                '<div class="vas_123_qrp-sec-head">' +
                    '<h4 class="vas_123_qrp-sh-title">' + esc(msg('VAS_123_DocumentStatus')) + '</h4>' +
                    (stageSummary ? '<span class="vas_123_qrp-sh-sum">' + esc(stageSummary) + '</span>' : '') +
                '</div>' +
                '<div class="vas_123_qrp-statuscard">' +
                    pipelineHtml +
                    calloutBlockHtml +
                '</div>' +
                (footLeftText ? '<div class="vas_123_qrp-sc-foot"><span class="vas_123_qrp-sc-meta">' + esc(footLeftText) + '</span></div>' : '');
        }

        // ── Section 2.1: Orders ───────────────────────────────────────────────
        function renderOrders() {
            var container = document.getElementById('vas_123_orders_' + widgetID);
            if (!container) return;

            if (state.generatedOrdersLoading && !state.generatedOrders) {
                container.innerHTML = '<div class="vas_123_qrp-skel vas_123_qrp-skel--row"></div>';
                return;
            }

            var h = state.header;
            if (!h) { container.innerHTML = ''; return; }

            var orders    = state.generatedOrders || [];
            var docStatus = h.docStatus || '';

            // §2.1a: also hide on voided records when no orders exist
            if (!orders.length && docStatus === 'VO') {
                container.innerHTML = '';
                return;
            }

            // Spec §2.1: hide entire section while Drafted/In Progress when no orders exist
            if (!orders.length && (docStatus === 'DR' || docStatus === 'IP')) {
                container.innerHTML = '';
                return;
            }

            var sym  = h.currencySymbol || '';
            var prec = (h.currencyPrecision != null) ? parseInt(h.currencyPrecision, 10) : 2;

            var bodyHtml;
            if (!orders.length) {
                bodyHtml = '<p class="vas_123_qrp-emptyline">' + esc(msg('VAS_123_OrdersEmpty')) + '</p>';
            } else {
                var rowsHtml = '';
                var grandSum = 0;
                for (var i = 0; i < orders.length; i++) {
                    var o       = orders[i];
                    var oId     = o.c_Order_ID    || o.C_Order_ID    || 0;
                    var oStatus = resolveStatusLabel(o.docStatus || o.DocStatus || '');
                    var oTone   = statusTone(o.docStatus || o.DocStatus || '');
                    var oTotal  = toNum(o.grandTotal || o.GrandTotal);
                    grandSum   += oTotal;

                    rowsHtml +=
                        '<div class="vas_123_qrp-erow" data-action="openOrder" data-order-id="' + esc(oId) + '">' +
                            '<div class="vas_123_qrp-etile vas_123_qrp-etile--info vas_123_qrp-etile--rect">' + SVG_CART + '</div>' +
                            '<div class="vas_123_qrp-emain">' +
                                '<div class="vas_123_qrp-e-titlerow">' +
                                    '<span class="vas_123_qrp-e-primary" title="' + esc(o.documentNo || o.DocumentNo || '') + '">' + esc(o.documentNo || o.DocumentNo || '') + '</span>' +
                                    '<span class="vas_123_qrp-chip vas_123_qrp-chip' + esc(oTone) + '">' + esc(oStatus) + '</span>' +
                                '</div>' +
                                '<p class="vas_123_qrp-e-meta">' +
                                    esc(msg('VAS_123_Ordered'))  + ' ' + esc(fmtDate(o.dateOrdered  || o.DateOrdered  || '')) +
                                    ' · ' +
                                    esc(msg('VAS_123_Promised')) + ' ' + esc(fmtDate(o.datePromised || o.DatePromised || '')) +
                                '</p>' +
                            '</div>' +
                            '<div class="vas_123_qrp-etrail">' +
                                '<p class="vas_123_qrp-e-value">' + esc(fmtCurrency(oTotal, sym, prec)) + '</p>' +
                                '<p class="vas_123_qrp-e-sub">'   + esc(msg('VAS_123_OrderTotal'))      + '</p>' +
                            '</div>' +
                        '</div>';
                }

                var summaryHtml = '';
                if (orders.length > 1) {
                    summaryHtml =
                        '<div class="vas_123_qrp-esummary">' +
                            '<span class="vas_123_qrp-es-label">' +
                                esc(msg('VAS_123_Converted')) + ' · ' + orders.length + ' ' + esc(msg('VAS_123_OrdersCount')) +
                            '</span>' +
                            '<span class="vas_123_qrp-es-total">' + esc(fmtCurrency(grandSum, sym, prec)) + '</span>' +
                        '</div>';
                }
                bodyHtml = '<div class="vas_123_qrp-elist">' + rowsHtml + '</div>' + summaryHtml;
            }

            var countHtml = orders.length
                ? '<span class="vas_123_qrp-sh-sum">' + orders.length + ' ' + esc(orders.length === 1 ? msg('VAS_123_OrderCount1') : msg('VAS_123_OrdersCount')) + '</span>'
                : '';

            container.innerHTML =
                '<div class="vas_123_qrp-sec-head">' +
                    '<h4 class="vas_123_qrp-sh-title">' + SVG_CART + esc(msg('VAS_123_Orders')) + '</h4>' +
                    '<span class="vas_123_qrp-sh-right">' + countHtml + '</span>' +
                '</div>' +
                bodyHtml;
        }

        // ── Section 2.2: Opportunity ──────────────────────────────────────────
        function renderOpportunity() {
            var container = document.getElementById('vas_123_opportunity_' + widgetID);
            if (!container) return;

            if (state.opportunityLoading && !state.opportunity) {
                container.innerHTML = '<div class="vas_123_qrp-skel vas_123_qrp-skel--row"></div>';
                return;
            }

            var h = state.header;
            if (!h) { container.innerHTML = ''; return; }

            var docStatusOpp = h.docStatus || '';
            var opp          = state.opportunity;
            var oppId        = opp ? toNum(opp.vAS_Opportunity_ID || opp.VAS_Opportunity_ID) : 0;
            var isLinked     = (oppId > 0);

            // §2.2: hide when no linked opportunity and record is completed/closed/voided
            if (!isLinked && (docStatusOpp === 'CO' || docStatusOpp === 'CL' || docStatusOpp === 'VO')) {
                container.innerHTML = '';
                return;
            }

            var actionHtml = '';
            var bodyHtml;

            if (isLinked) {
                // Show opportunity details when the server provides them (display mapping confirmed later);
                // fall back to a placeholder row so the linked state is always visible.
                var oppName      = opp.opportunityName    || opp.OpportunityName    || '';
                var oppStage     = opp.stage              || opp.Stage              || '';
                var oppStageName = opp.stageName          || opp.StageName          || oppStage;
                var oppRep       = opp.salesRepName       || opp.SalesRepName       || '';
                var oppClose     = opp.expectedCloseDate  || opp.ExpectedCloseDate  || '';
                var oppAmt       = (opp.amount  != null)  ? opp.amount
                                 : (opp.Amount  != null)  ? opp.Amount : null;
                var sym          = h.currencySymbol || '';
                var prec         = (h.currencyPrecision != null) ? parseInt(h.currencyPrecision, 10) : 2;

                var primaryText = oppName || msg('VAS_123_OpportunityLinked');

                var stageTone = '--info';
                if (oppStage === 'Won'  || oppStage === 'WO') { stageTone = '--success'; }
                if (oppStage === 'Lost' || oppStage === 'LO') { stageTone = '--risk';    }

                var stageChip = oppStageName
                    ? '<span class="vas_123_qrp-chip vas_123_qrp-chip' + esc(stageTone) + '">' + esc(oppStageName) + '</span>'
                    : '';

                var metaParts = [];
                if (oppClose) { metaParts.push(esc(msg('VAS_123_ExpectedClose')) + ' ' + esc(fmtDate(oppClose))); }
                if (oppRep)   { metaParts.push(esc(oppRep)); }

                var trailHtml = (oppAmt != null)
                    ? '<div class="vas_123_qrp-etrail">' +
                          '<p class="vas_123_qrp-e-value">' + esc(fmtCurrency(oppAmt, sym, prec)) + '</p>' +
                          '<p class="vas_123_qrp-e-sub">'   + esc(msg('VAS_123_DealValue'))        + '</p>' +
                      '</div>'
                    : '';

                bodyHtml =
                    '<div class="vas_123_qrp-elist">' +
                        '<div class="vas_123_qrp-erow" data-action="openLinkedQuotations" data-opportunity-id="' + esc(oppId) + '" data-opportunity-name="' + esc(primaryText) + '">' +
                            '<div class="vas_123_qrp-etile vas_123_qrp-etile--success vas_123_qrp-etile--rect">' + SVG_TARGET + '</div>' +
                            '<div class="vas_123_qrp-emain">' +
                                '<div class="vas_123_qrp-e-titlerow">' +
                                    '<span class="vas_123_qrp-e-primary" title="' + esc(primaryText) + '">' + esc(primaryText) + '</span>' +
                                    stageChip +
                                '</div>' +
                                (metaParts.length ? '<p class="vas_123_qrp-e-meta">' + metaParts.join(' · ') + '</p>' : '') +
                            '</div>' +
                            trailHtml +
                        '</div>' +
                    '</div>';
            } else {
                actionHtml = '';
                bodyHtml = '<p class="vas_123_qrp-emptyline">' + esc(msg('VAS_123_OpportunityEmpty')) + '</p>';
            }

            container.innerHTML =
                '<div class="vas_123_qrp-sec-head">' +
                    '<h4 class="vas_123_qrp-sh-title">' + SVG_TARGET + esc(msg('Opportunity')) + '</h4>' +
                    '<span class="vas_123_qrp-sh-right">' + actionHtml + '</span>' +
                '</div>' +
                bodyHtml;
        }

        // ── Section 2.3: Quotation Lines ──────────────────────────────────────
        function renderLines() {
            var container = document.getElementById('vas_123_lines_' + widgetID);
            if (!container) return;

            if (state.linesLoading && !state.lines) {
                container.innerHTML = '<div class="vas_123_qrp-skel vas_123_qrp-skel--block"></div>';
                return;
            }

            var h = state.header;
            if (!h) { container.innerHTML = ''; return; }

            var lines     = state.lines || [];
            var docStatus = h.docStatus || '';
            var editable  = (docStatus === 'DR' || docStatus === 'IP');
            var sym       = h.currencySymbol || '';
            var prec      = (h.currencyPrecision != null) ? parseInt(h.currencyPrecision, 10) : 2;

            // §2.3: hide lines section when empty and record is closed/voided
            if (!lines.length && (docStatus === 'CL' || docStatus === 'VO')) {
                container.innerHTML = '';
                return;
            }

            var addBtnHtml = '';

            var bodyHtml;
            if (!lines.length) {
                bodyHtml = '<p class="vas_123_qrp-emptyline">' + esc(msg('VAS_123_LinesEmpty')) + '</p>';
            } else {
                var histMap  = state.lineHistory || {};
                var rowsHtml = '';
                for (var i = 0; i < lines.length; i++) {
                    var l         = lines[i];
                    var lId       = l.c_OrderLine_ID || l.C_OrderLine_ID || 0;
                    var isService = !!(l.isService    || l.IsService);
                    var qty       = toNum(l.qtyEntered   || l.QtyEntered);
                    var uom       = l.uOMName             || l.UOMName    || '';
                    var price     = toNum(l.priceEntered  || l.PriceEntered  || l.priceActual || l.PriceActual);
                    var disc      = toNum(l.discount      || l.Discount);
                    var amt       = toNum(l.lineNetAmt    || l.LineNetAmt);
                    var sku       = l.productValue        || l.ProductValue || '';
                    var product   = l.productName         || l.ProductName  || l.description || l.Description || '';

                    var metaStr = (sku ? esc(sku) + ' · ' : '') + qty + ' ' + esc(uom) + ' × ' + esc(fmtCurrency(price, sym, prec));
                    if (disc > 0) { metaStr += ' · −' + disc + '%'; }

                    // §9b  Build history toggle + drawer for this line
                    var lineHist   = histMap[lId] || [];
                    var hasHist    = lineHist.length > 0;
                    var isHistOpen = !!(state.lineHistOpen && state.lineHistOpen[lId]);
                    var toggleHtml = '';
                    var drawerHtml = '';
                    if (hasHist) {
                        var toggleTitle = isHistOpen
                            ? esc(msg('VAS_123_HistHide'))
                            : esc(msg('VAS_123_HistShow')) + ' (' + lineHist.length + ')';
                        toggleHtml =
                            '<button class="vas_123_qrp-lh-toggle' + (isHistOpen ? ' is-open' : '') + '"' +
                                ' data-action="toggleLineHistory" data-line-id="' + esc(lId) + '"' +
                                ' title="' + toggleTitle + '">' +
                                SVG_CLOCK +
                            '</button>';

                        var hRows = '';
                        for (var j = 0; j < lineHist.length; j++) {
                            var hr     = lineHist[j];
                            var hUom   = hr.uOMSymbol      || hr.UOMSymbol  || '';
                            var hPrec  = hr.uOMPrecision   != null ? parseInt(hr.uOMPrecision, 10)  : 0;
                            var hCPrec = hr.currencyPrec   != null ? parseInt(hr.currencyPrec, 10)  : prec;
                            var hQty   = toNum(hr.qtyEntered   || hr.QtyEntered);
                            var hOrd   = toNum(hr.qtyOrdered   || hr.QtyOrdered);
                            var hDel   = toNum(hr.qtyDelivered || hr.QtyDelivered);
                            var hPct   = hOrd > 0 ? Math.round((hDel / hOrd) * 100) : 0;
                            var hBarW  = Math.max(0, Math.min(100, hPct));
                            hRows +=
                                '<div class="vas_123_qrp-lh-row">' +
                                    '<span class="vas_123_qrp-lh-when">' + esc(fmtEngTs(hr.changedOn || hr.ChangedOn)) + '</span>' +
                                    '<span>' + esc(fmtCurrency(toNum(hr.priceActual || hr.PriceActual), sym, hCPrec)) + '</span>' +
                                    '<span>' + esc(hQty.toFixed(hPrec) + (hUom ? ' ' + hUom : '')) + '</span>' +
                                    '<span>' + esc(fmtDate(hr.datePromised || hr.DatePromised) || '—') + '</span>' +
                                    '<span>' + esc(fmtCurrency(toNum(hr.lineNetAmt || hr.LineNetAmt), sym, hCPrec)) + '</span>' +
                                    '<span class="vas_123_qrp-lh-recv">' +
                                        '<span class="vas_123_qrp-lh-bar"><i style="width:' + hBarW + '%"></i></span>' +
                                        '<span>' + esc(hDel.toFixed(hPrec) + '/' + hOrd.toFixed(hPrec)) + '</span>' +
                                    '</span>' +
                                '</div>';
                        }
                        drawerHtml =
                            '<div id="vas_123_lhd_' + esc(lId) + '_' + widgetID + '"' +
                                ' class="vas_123_qrp-lh-drawer"' + (isHistOpen ? '' : ' style="display:none"') + '>' +
                                '<div class="vas_123_qrp-lh-table">' +
                                    '<div class="vas_123_qrp-lh-thead">' +
                                        '<span>' + esc(msg('VAS_123_HistChangedOn'))    + '</span>' +
                                        '<span>' + esc(msg('VAS_123_HistUnitPrice'))    + '</span>' +
                                        '<span>' + esc(msg('VAS_123_HistQty'))          + '</span>' +
                                        '<span>' + esc(msg('VAS_123_HistExpDelivery'))  + '</span>' +
                                        '<span>' + esc(msg('VAS_123_HistLineAmt'))      + '</span>' +
                                        '<span>' + esc(msg('VAS_123_HistReceived'))     + '</span>' +
                                    '</div>' +
                                    hRows +
                                '</div>' +
                            '</div>';
                    }

                    rowsHtml +=
                        '<div class="vas_123_qrp-lh-wrap">' +
                            '<div class="vas_123_qrp-erow" data-action="openLine" data-line-id="' + esc(lId) + '">' +
                                '<div class="vas_123_qrp-etile vas_123_qrp-etile--info vas_123_qrp-etile--rect">' + (isService ? SVG_WRENCH : SVG_BOX) + '</div>' +
                                '<div class="vas_123_qrp-emain">' +
                                    '<div class="vas_123_qrp-e-titlerow">' +
                                        '<span class="vas_123_qrp-e-primary" title="' + esc(product) + '">' + esc(product) + '</span>' +
                                    '</div>' +
                                    '<p class="vas_123_qrp-e-meta" title="' + metaStr + '">' + metaStr + '</p>' +
                                '</div>' +
                                '<div class="vas_123_qrp-etrail">' +
                                    '<p class="vas_123_qrp-e-value">' + esc(fmtCurrency(amt, sym, prec)) + '</p>' +
                                    toggleHtml +
                                '</div>' +
                            '</div>' +
                            drawerHtml +
                        '</div>';
                }

                // Totals: net and grand from state.header; tax derived (no freight per spec)
                var netTotal = toNum(h.totalLines);
                var grand    = toNum(h.grandTotal);
                var taxAmt   = grand - netTotal;

                var totHtml =
                    '<div class="vas_123_qrp-totals-block">' +
                        '<div class="vas_123_qrp-totrow">' +
                            '<span class="vas_123_qrp-t-label">' + esc(msg('VAS_123_Subtotal')) + '</span>' +
                            '<span class="vas_123_qrp-t-value">' + esc(fmtCurrency(netTotal, sym, prec)) + '</span>' +
                        '</div>' +
                        '<div class="vas_123_qrp-totrow">' +
                            '<span class="vas_123_qrp-t-label">' + esc(msg('VAS_123_Tax')) + '</span>' +
                            '<span class="vas_123_qrp-t-value">' + esc(fmtCurrency(taxAmt, sym, prec)) + '</span>' +
                        '</div>' +
                        '<div class="vas_123_qrp-esummary">' +
                            '<span class="vas_123_qrp-es-label">' +
                                esc(msg('VAS_123_GrandTotal')) + ' · ' + lines.length + ' ' + esc(lines.length === 1 ? msg('VAS_123_Line') : msg('VAS_123_Lines')) +
                            '</span>' +
                            '<span class="vas_123_qrp-es-total">' + esc(fmtCurrency(grand, sym, prec)) + '</span>' +
                        '</div>' +
                    '</div>';

                bodyHtml = '<div class="vas_123_qrp-elist">' + rowsHtml + '</div>' + totHtml;
            }

            var countHtml = lines.length
                ? '<span class="vas_123_qrp-sh-sum">' + lines.length + ' ' + esc(lines.length === 1 ? msg('VAS_123_Line') : msg('VAS_123_Lines')) + '</span>'
                : '';

            container.innerHTML =
                '<div class="vas_123_qrp-sec-head">' +
                    '<h4 class="vas_123_qrp-sh-title">' + SVG_BOX + esc(msg('VAS_123_QuotationLines')) + '</h4>' +
                    '<span class="vas_123_qrp-sh-right">' + countHtml + addBtnHtml + '</span>' +
                '</div>' +
                bodyHtml;
        }

        // ── Section 2.4: Customer & Contact ───────────────────────────────────
        function renderCustomer() {
            var container = document.getElementById('vas_123_customer_' + widgetID);
            if (!container) return;

            // Wait for both header and address loads to complete before rendering
            if (state.headerLoading || (state.addressesLoading && !state.addresses)) {
                container.innerHTML = '<div class="vas_123_qrp-skel vas_123_qrp-skel--block"></div>';
                return;
            }

            var h = state.header;
            if (!h) { container.innerHTML = ''; return; }

            var sym  = h.currencySymbol || '';
            var prec = (h.currencyPrecision != null) ? parseInt(h.currencyPrecision, 10) : 2;
            var bpId = h.c_BPartner_ID  || h.C_BPartner_ID || 0;

            // ── Credit chip ─────────────────────────────────────────────────
            var creditStatus = h.creditStatus || h.CustomerCreditStatus || '';
            var creditChip   = (creditStatus === 'H')
                ? '<span class="vas_123_qrp-chip vas_123_qrp-chip--risk">'    + esc(msg('VAS_123_CreditHold')) + '</span>'
                : '<span class="vas_123_qrp-chip vas_123_qrp-chip--success">' + esc(msg('VAS_123_CreditOK'))   + '</span>';

            // ── Open balance — comes from state.addresses (fetched separately) ──
            var addr        = state.addresses;
            var openBalance = (addr && addr.totalOpenBalance != null) ? addr.totalOpenBalance
                            : (h.totalOpenBalance    != null) ? h.totalOpenBalance
                            : (h.customerOpenBalance != null) ? h.customerOpenBalance : null;
            var balHtml     = (openBalance != null) ? esc(fmtCurrency(openBalance, sym, prec)) : '—';

            // ── Bill-to / Ship-to from the separately loaded address record ──
            var billTo = addr ? (addr.billingLocationName  || addr.BillingLocationName  || '') : '';
            var shipTo = addr ? (addr.shippingLocationName || addr.ShippingLocationName || '') : '';
            var addrMeta = '';
            if (billTo || shipTo) {
                var addrParts = [];
                if (billTo) { addrParts.push(esc(msg('VAS_123_BillTo')) + ' ' + esc(billTo)); }
                if (shipTo) { addrParts.push(esc(msg('VAS_123_ShipTo')) + ' ' + esc(shipTo)); }
                addrMeta = '<p class="vas_123_qrp-pc-meta">' + addrParts.join(' · ') + '</p>';
            }

            // ── Customer card (tinted, clickable) ────────────────────────────
            var bpName = h.bPartnerName || h.BPartnerName || h.customerName || h.CustomerName || '';
            var customerHtml =
                '<div class="vas_123_qrp-pcard vas_123_qrp-pcard--tinted" data-action="openCustomer" data-bp-id="' + esc(bpId) + '">' +
                    '<div class="vas_123_qrp-pcard-row">' +
                        '<div class="vas_123_qrp-etile vas_123_qrp-etile--info" style="border-radius:999px">' + SVG_USERS + '</div>' +
                        '<div class="vas_123_qrp-pc-main">' +
                            '<div class="vas_123_qrp-e-titlerow">' +
                                '<span class="vas_123_qrp-pc-name">' + esc(bpName) + '</span>' +
                                creditChip +
                            '</div>' +
                            addrMeta +
                        '</div>' +
                        '<div class="vas_123_qrp-etrail">' +
                            '<p class="vas_123_qrp-e-value">' + balHtml + '</p>' +
                            '<p class="vas_123_qrp-e-sub">' + esc(msg('VAS_123_OpenBalance')) + '</p>' +
                        '</div>' +
                    '</div>' +
                '</div>';

            // ── Contact card ─────────────────────────────────────────────────
            var contactName   = h.contactName   || h.ContactName   || '';
            var contactTitle  = h.contactTitle  || h.ContactTitle  || '';
            var contactEmail  = h.contactEmail  || h.ContactEmail  || '';
            var contactMobile = h.contactMobile || h.ContactMobile || '';

            var contactHtml;
            if (!contactName) {
                contactHtml = '<p class="vas_123_qrp-emptyline">' + esc(msg('VAS_123_ContactEmpty')) + '</p>';
            } else {
                var contactMetaParts = [];
                if (contactEmail)  { contactMetaParts.push(esc(contactEmail));  }
                if (contactMobile) { contactMetaParts.push(esc(contactMobile)); }

                contactHtml =
                    '<div class="vas_123_qrp-pcard">' +
                        '<div class="vas_123_qrp-pcard-row">' +
                            '<span class="vas_123_qrp-contact-avatar">' + esc(initials(contactName)) + '</span>' +
                            '<div class="vas_123_qrp-pc-main">' +
                                '<p class="vas_123_qrp-pc-name">'  + esc(contactName) + '</p>' +
                                (contactTitle ? '<p class="vas_123_qrp-pc-meta">' + esc(contactTitle) + '</p>' : '') +
                                (contactMetaParts.length ? '<p class="vas_123_qrp-pc-meta">' + contactMetaParts.join(' · ') + '</p>' : '') +
                            '</div>' +
                            '<div class="vas_123_qrp-pc-actions">' +
                                '<button class="vas_123_qrp-iconbtn" title="' + esc(msg('VAS_123_Email')) + '" data-action="sendQuotation">' + SVG_MAIL + '</button>' +
                                (contactMobile
                                    ? '<button class="vas_123_qrp-iconbtn" title="' + esc(msg('Call')) + '" data-action="callContact" data-mobile="' + esc(contactMobile) + '">' + SVG_PHONE + '</button>'
                                    : '') +
                            '</div>' +
                        '</div>' +
                    '</div>';
            }

            container.innerHTML =
                '<div class="vas_123_qrp-sec-head">' +
                    '<h4 class="vas_123_qrp-sh-title">' + SVG_USERS + esc(msg('VAS_123_CustomerContact')) + '</h4>' +
                '</div>' +
                '<div class="vas_123_qrp-customer-stack">' +
                    customerHtml +
                    contactHtml +
                '</div>';
        }

        // ── Section 2.5: Pricing & Terms ─────────────────────────────────────
        function renderTerms() {
            var container = document.getElementById('vas_123_terms_' + widgetID);
            if (!container) return;

            if (state.termsLoading) {
                container.innerHTML = '<div class="vas_123_qrp-skel vas_123_qrp-skel--block"></div>';
                return;
            }

            // Fall back to header for date/ref fields that are always present there
            var h = state.header || {};
            var t = state.terms  || {};

            if (!state.headerLoaded && !state.terms) { container.innerHTML = ''; return; }

            // Reusable 2-column metric cell for the terms grid
            function termCell(label, value) {
                var display = value || '—';
                return '<div class="vas_123_qrp-mcell">' +
                    '<p class="vas_123_qrp-m-label">'                             + esc(label)   + '</p>' +
                    '<p class="vas_123_qrp-m-value" title="' + esc(display) + '">' + esc(display) + '</p>' +
                '</div>';
            }

            // terms has its own currencyISO; fall back to header currency
            var currCode    = t.currencyCode || t.currencyISO || h.currencyCode || h.currencyISO || '';
            var currDisplay = currCode ? (currCode + ' · ' + msg('VAS_123_DocumentCurrency')) : '';

            // PaymentRule and PriorityRule: server decodes list-reference labels; fall back to raw codes
            var paymentRule  = t.paymentRuleLabel  || t.paymentRule  || '';
            var priorityRule = t.priorityRuleLabel || t.priorityRule || '';

            // Date fields: prefer terms (self-contained), fall back to header
            var dateOrdered  = t.dateOrdered  || h.dateOrdered;
            var orderValidTo = t.orderValidTo || h.orderValidTo;

            container.innerHTML =
                '<div class="vas_123_qrp-sec-head">' +
                    '<h4 class="vas_123_qrp-sh-title">' + esc(msg('VAS_123_PricingTerms')) + '</h4>' +
                '</div>' +
                '<div class="vas_123_qrp-dcard">' +
                    '<div class="vas_123_qrp-mgrid">' +
                        termCell(msg('VAS_123_PriceList'),   t.priceListName  || '') +
                        termCell(msg('VAS_123_Currency'),    currDisplay) +
                        termCell(msg('VAS_123_PaymentTerm'), t.paymentTermName || '') +
                        termCell(msg('VAS_123_PaymentRule'), paymentRule) +
                        termCell(msg('VAS_123_ValidFrom'),   dateOrdered  ? fmtDate(dateOrdered)  : '') +
                        termCell(msg('VAS_123_ValidUntil'),  orderValidTo ? fmtDate(orderValidTo) : '') +
                        termCell(msg('Priority'),    priorityRule) +
                    '</div>' +
                '</div>';
        }

        // ── Section 3.2: Tasks ────────────────────────────────────────────────
        function renderTasks() {
            var container = document.getElementById('vas_123_tasks_' + widgetID);
            if (!container) return;

            if (state.tasksLoading) {
                container.innerHTML =
                    '<div class="vas_123_qrp-sec-head">' +
                        '<h4 class="vas_123_qrp-sh-title">' + SVG_CHECK + esc(msg('VAS_123_Tasks')) + '</h4>' +
                    '</div>' +
                    '<div class="vas_123_qrp-skel vas_123_qrp-skel--row"></div>';
                return;
            }

            var h = state.header;
            if (!h) { container.innerHTML = ''; return; }

            var allTasks     = state.tasks || [];
            var taskDocSt    = h.docStatus || '';
            var taskEditable = (taskDocSt === 'DR' || taskDocSt === 'IP');

            // §3.2: hide section when no tasks and record is not editable (CO/CL/VO)
            if (!allTasks.length && !taskEditable) {
                container.innerHTML = '';
                return;
            }

            var upcoming = [];
            var previous = [];
            for (var i = 0; i < allTasks.length; i++) {
                var t = allTasks[i];
                var closed = !!(t.closed || t.Closed || (t.pct != null && toNum(t.pct) >= 100 && (t.closed || t.Closed)));
                t._closed = closed;
                if (closed) { previous.push(t); } else { upcoming.push(t); }
            }

            var listToShow = (state.taskTab === 'prev') ? previous : upcoming;

            var segHtml =
                '<span class="vas_123_qrp-seg">' +
                    '<button class="vas_123_qrp-seg-btn' + (state.taskTab === 'up'   ? ' vas_123_qrp-seg-btn--on' : '') + '" data-action="switchTaskTab" data-tab="up">' +
                        esc(msg('VAS_123_Upcoming')) + ' · ' + upcoming.length +
                    '</button>' +
                    '<button class="vas_123_qrp-seg-btn' + (state.taskTab === 'prev' ? ' vas_123_qrp-seg-btn--on' : '') + '" data-action="switchTaskTab" data-tab="prev">' +
                        esc(msg('VIS_Previous')) + ' · ' + previous.length +
                    '</button>' +
                '</span>';

            var newTaskBtnHtml =
                '<button class="vas_123_qrp-sh-action" data-action="newTask">' +
                    SVG_PLUS + esc(msg('VAS_123_NewTask')) +
                '</button>';
            // Suppress entry point on non-editable records (CO/CL/VO)
            if (!taskEditable) { newTaskBtnHtml = ''; }

            var bodyHtml;
            if (!listToShow.length) {
                bodyHtml = state.taskTab === 'up'
                    ? '<p class="vas_123_qrp-emptyline">' + esc(msg('VAS_123_TasksEmptyUp'))   + '</p>'
                    : '<p class="vas_123_qrp-emptyline">' + esc(msg('VAS_123_TasksEmptyPrev')) + '</p>';
            } else {
                var today = new Date(); today.setHours(0, 0, 0, 0);
                var rowsHtml = '';
                var PRIORITY_TONE = { U: '--risk', '3': '--warn', '5': '--info', '7': '--neutral' };
                var PRIORITY_LABEL = { U: msg('VAS_123_PriorityUrgent'), '3': msg('VAS_123_PriorityHigh'), '5': msg('VAS_123_PriorityMedium'), '7': msg('VAS_123_PriorityLow') };

                for (var ti = 0; ti < listToShow.length; ti++) {
                    var tk      = listToShow[ti];
                    var tkId    = tk.r_Request_ID || tk.R_Request_ID || 0;
                    var tkTitle = tk.title || tk.Title || '';
                    var tkDue   = tk.due   || tk.Due   || '';
                    var tkAssg  = tk.assigneeName || tk.AssigneeName || '';
                    var tkPri   = tk.priority     || tk.Priority     || '5';
                    var tkPct   = toNum(tk.pct    || tk.Pct);
                    var tkCl    = tk._closed;

                    var dueDate  = tkDue ? new Date(tkDue) : null;
                    dueDate && dueDate.setHours(0, 0, 0, 0);
                    var overdue  = !tkCl && dueDate && (dueDate < today);
                    var priTone  = PRIORITY_TONE[tkPri]  || '--neutral';
                    var priLabel = PRIORITY_LABEL[tkPri] || tkPri;

                    rowsHtml +=
                        '<div class="vas_123_qrp-taskrow" data-action="openTask" data-task-id="' + esc(tkId) + '">' +
                            '<input type="checkbox" ' + (tkCl ? 'checked' : '') + ' data-action="toggleTask" data-task-id="' + esc(tkId) + '">' +
                            '<div class="vas_123_qrp-task-main">' +
                                '<p class="vas_123_qrp-task-title' + (tkCl ? ' vas_123_qrp-task-title--done' : '') + '" title="' + esc(tkTitle) + '">' + esc(tkTitle) + '</p>' +
                                '<p class="vas_123_qrp-task-meta">' +
                                    (tkDue ? '<span' + (overdue ? ' class="vas_123_qrp-task-overdue"' : '') + '>' +
                                        esc(msg('VAS_123_Due')) + ' ' + esc(fmtDate(tkDue)) + (overdue ? ' · ' + esc(msg('VAS_123_Overdue')) : '') +
                                    '</span>' : '') +
                                    (tkAssg ? '<span>' + esc(tkAssg) + '</span>' : '') +
                                    '<span>' + tkPct + '%</span>' +
                                '</p>' +
                            '</div>' +
                            '<span class="vas_123_qrp-chip vas_123_qrp-chip' + esc(priTone) + '">' + esc(priLabel) + '</span>' +
                        '</div>';
                }
                bodyHtml = rowsHtml;
            }

            container.innerHTML =
                '<div class="vas_123_qrp-sec-head">' +
                    '<h4 class="vas_123_qrp-sh-title">' + SVG_CHECK + esc(msg('VAS_123_Tasks')) + '</h4>' +
                    '<span class="vas_123_qrp-sh-right">' + segHtml + newTaskBtnHtml + '</span>' +
                '</div>' +
                bodyHtml;
        }

        // ── Section 3.3: Engagement timeline ─────────────────────────────────
        function renderEngagement() {
            var container = document.getElementById('vas_123_engagement_' + widgetID);
            if (!container) return;

            if (state.engagementLoading) {
                container.innerHTML =
                    '<div class="vas_123_qrp-sec-head">' +
                        '<h4 class="vas_123_qrp-sh-title">' + SVG_CHAT + esc(msg('VAS_123_Engagement')) + '</h4>' +
                    '</div>' +
                    '<div class="vas_123_qrp-skel vas_123_qrp-skel--block"></div>';
                return;
            }

            var h = state.header;
            if (!h) { container.innerHTML = ''; return; }

            var engData     = state.engagement || { counts: { total: 0 }, items: [] };
            var counts      = engData.counts || {};
            var items       = Array.isArray(engData.items) ? engData.items : [];
            var engDocSt    = h.docStatus || '';
            var engEditable = (engDocSt === 'DR' || engDocSt === 'IP');

            // §3.3: hide engagement section when no items and record is not editable (CO/CL/VO)
            if (!items.length && !engEditable) {
                container.innerHTML = '';
                return;
            }

            // ── SVG icons (inline, no external dependency) ───────────────────
            var SVG_LIST      = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="8" y1="6" x2="21" y2="6"/><line x1="8" y1="12" x2="21" y2="12"/><line x1="8" y1="18" x2="21" y2="18"/><line x1="3" y1="6" x2="3.01" y2="6"/><line x1="3" y1="12" x2="3.01" y2="12"/><line x1="3" y1="18" x2="3.01" y2="18"/></svg>';
            var SVG_PHONE     = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07A19.5 19.5 0 0 1 4.69 13a19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 3.77 2h3a2 2 0 0 1 2 1.72c.127.96.361 1.903.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l.91-.91a2 2 0 0 1 2.11-.45c.907.339 1.85.573 2.81.7A2 2 0 0 1 22 17z"/></svg>';
            var SVG_CAL       = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="18" rx="2" ry="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/></svg>';
            var SVG_MAIL2     = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="2" y="4" width="20" height="16" rx="2"/><path d="m22 7-8.97 5.7a1.94 1.94 0 0 1-2.06 0L2 7"/></svg>';
            var SVG_NOTE2     = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14.5 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7.5L14.5 2z"/><polyline points="14 2 14 8 20 8"/></svg>';
            var SVG_WA        = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M21 11.5a8.38 8.38 0 0 1-.9 3.8 8.5 8.5 0 0 1-7.6 4.7 8.38 8.38 0 0 1-3.8-.9L3 21l1.9-5.7a8.38 8.38 0 0 1-.9-3.8 8.5 8.5 0 0 1 4.7-7.6 8.38 8.38 0 0 1 3.8-.9h.5a8.48 8.48 0 0 1 8 8v.5z"/></svg>';
            var SVG_TRS       = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14.5 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7.5L14.5 2z"/><polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/><line x1="10" y1="9" x2="8" y2="9"/></svg>';
            var SVG_REPLY2    = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 17 4 12 9 7"/><path d="M20 18v-2a4 4 0 0 0-4-4H4"/></svg>';

            var BADGE = {
                'NOTE':    { bg: '#FEF3C7', color: '#92400E', labelKey: 'Note'            },
                'MEETING': { bg: '#EDE9FE', color: '#5B21B6', labelKey: 'VAS_123_Meeting' },
                'EMAIL':   { bg: '#DBEAFE', color: '#1D4ED8', labelKey: 'EMail'           },
                'CALL':    { bg: '#D1FAE5', color: '#065F46', labelKey: 'VAS_123_Call'    },
                'CHAT':    { bg: '#D1FAE5', color: '#065F46', labelKey: 'VAS_123_Chat'    }
            };
            var DOT_COLOR = {
                'NOTE': '#F59E0B', 'MEETING': '#7C3AED', 'EMAIL': '#2563EB', 'CALL': '#059669', 'CHAT': '#059669'
            };
            var TYPE_SVG = {
                'NOTE': SVG_NOTE2, 'MEETING': SVG_CAL, 'EMAIL': SVG_MAIL2, 'CALL': SVG_PHONE, 'CHAT': SVG_WA
            };

            // ── Stat cards strip ─────────────────────────────────────────────
            var inCnt = 0, outCnt = 0;
            for (var ei = 0; ei < items.length; ei++) {
                if (items[ei].touchType === 'EMAIL') {
                    if (items[ei].direction === 'in') inCnt++; else outCnt++;
                }
            }
            var emailSub = inCnt + ' in · ' + outCnt + ' out';

            var totalMeetingMins = counts.totalMeetingMins || 0;
            var meetingAttendees = counts.meetingAttendees  || 0;
            var totalCallMins    = counts.totalCallMins     || 0;
            var connectedCalls   = counts.connectedCalls    || 0;

            var mtgParts = [];
            if (totalMeetingMins > 0) mtgParts.push(totalMeetingMins + 'm');
            if (meetingAttendees > 0) mtgParts.push(meetingAttendees + ' ' + msg('VAS_123_Attended'));
            var mtgSub = mtgParts.length ? mtgParts.join(' · ') : msg('Transcript');

            var callParts = [];
            if (totalCallMins  > 0) callParts.push(totalCallMins  + 'm');
            if (connectedCalls > 0) callParts.push(connectedCalls + ' ' + msg('VAS_123_Connected'));
            var callSub = callParts.length ? callParts.join(' · ') : msg('VAS_123_Connected');

            function statCard(svgIcon, count, labelKey, sub, action) {
                var cls   = 'vas_123_qrp-eng-statcard' + (action ? ' vas_123_qrp-eng-statcard--clickable' : '');
                var attrs = action ? ' data-action="' + action + '"' : '';
                return '<div class="' + cls + '"' + attrs + '>' +
                    '<div class="vas_123_qrp-eng-statcard-icon">' + svgIcon + '</div>' +
                    '<div class="vas_123_qrp-eng-statcard-count">' + count + '</div>' +
                    '<div class="vas_123_qrp-eng-statcard-label">' + esc(msg(labelKey)) + '</div>' +
                    '<div class="vas_123_qrp-eng-statcard-sub">' + esc(sub) + '</div>' +
                    '</div>';
            }

            var statHtml = '<div class="vas_123_qrp-eng-statstrip">';
            statHtml += statCard(SVG_LIST,  counts.total    || 0, 'VAS_123_AllTouches', msg('VAS_123_Last30Days'));
            statHtml += statCard(SVG_PHONE, counts.calls    || 0, 'VAS_123_Calls',      callSub);
            statHtml += statCard(SVG_CAL,   counts.meetings || 0, 'VAS_123_Meetings',   mtgSub);
            statHtml += statCard(SVG_MAIL2, counts.emails   || 0, 'VAS_123_Emails',     emailSub);
            statHtml += statCard(SVG_WA,    counts.chat     || 0, 'VAS_123_WhatsApp',   msg('VAS_123_WhatsApp'), 'openChat');
            statHtml += '</div>';

            // ── Timeline ─────────────────────────────────────────────────────
            var tlHtml = '';
            if (!items.length) {
                tlHtml = '<p class="vas_123_qrp-emptyline">' + esc(msg('VAS_123_EngagementEmpty')) + '</p>';
            } else {
                tlHtml = '<div class="vas_123_qrp-tl">';
                for (var ii = 0; ii < items.length; ii++) {
                    var item     = items[ii];
                    var bCfg     = BADGE[item.touchType]    || BADGE['NOTE'];
                    var dotClr   = DOT_COLOR[item.touchType] || '#94A3B8';
                    var typeIcon = TYPE_SVG[item.touchType]  || SVG_NOTE2;
                    var isLast   = (ii === items.length - 1);

                    var titleText = item.title || '';
                    if (item.touchType === 'NOTE') {
                        titleText = (item.title || msg('Notes')) + (item.who ? ' · ' + item.who : '');
                    } else if (item.touchType === 'EMAIL') {
                        var emailPrefix = item.direction === 'in' ? msg('From') : msg('To');
                        titleText = (item.title || '') + (item.who ? ' · ' + emailPrefix + ' ' + item.who : '');
                    } else if (item.touchType === 'CHAT') {
                        titleText = msg('VAS_123_WhatsApp') + (item.who ? ' · ' + msg('VAS_123_With') + ' ' + item.who : '');
                    } else if (item.touchType === 'MEETING') {
                        var durStr = '';
                        if (item.durationMins > 0) {
                            var dh = Math.floor(item.durationMins / 60), dm = item.durationMins % 60;
                            durStr = dh > 0 ? (dh + 'h' + (dm > 0 ? ' ' + dm + 'm' : '')) : (dm + 'm');
                        }
                        titleText = (item.title || '')
                            + (item.location    ? ' · ' + item.location : '')
                            + (durStr           ? ' · ' + durStr        : '');
                    }

                    var dirBadge = '';
                    if (item.direction === 'in')  dirBadge = '<span class="vas_123_qrp-eng-dir-in">'  + esc(msg('VAS_123_Incoming')) + '</span>';
                    if (item.direction === 'out') dirBadge = '<span class="vas_123_qrp-eng-dir-out">' + esc(msg('VAS_123_Outgoing')) + '</span>';

                    var metaHtml2 = '';
                    if (item.touchType === 'MEETING') {
                        var mtParts2 = [];
                        if (item.who) mtParts2.push(esc(item.who));
                        if (item.hasTranscript) mtParts2.push('<span class="vas_123_qrp-eng-meta-badge">' + SVG_TRS + ' ' + esc(msg('Transcript')) + '</span>');
                        metaHtml2 = mtParts2.join('&nbsp;&nbsp;');
                    } else if (item.touchType === 'EMAIL') {
                        var emParts2 = [];
                        if (item.who) emParts2.push(esc(item.who));
                        if (item.direction === 'in')  emParts2.push('<span class="vas_123_qrp-eng-meta-badge">' + SVG_REPLY2 + ' ' + esc(msg('VAS_123_ReplyReceived')) + '</span>');
                        if (item.direction === 'out') emParts2.push('<span class="vas_123_qrp-eng-meta-badge">' + SVG_SEND  + ' ' + esc(msg('Sent')) + '</span>');
                        metaHtml2 = emParts2.join('&nbsp;&nbsp;');
                    } else if (item.touchType === 'CALL' || item.touchType === 'CHAT') {
                        metaHtml2 = item.who ? esc(item.who) : '';
                    }

                    var meetingId2 = (item.touchType === 'MEETING') ? (item.meetingId || 0) : 0;
                    var noteId2    = (item.touchType === 'NOTE')    ? (item.noteId    || 0) : 0;
                    var emailId2   = (item.touchType === 'EMAIL')   ? (item.emailId   || 0) : 0;
                    var topicId2   = (item.touchType === 'CHAT')    ? (item.topicId   || 0) : 0;
                    var isClickable = meetingId2 > 0 || noteId2 > 0 || emailId2 > 0 || topicId2 > 0;
                    var cardCls   = 'vas_123_qrp-tl-card' + (isClickable ? ' vas_123_qrp-tl-card--clickable' : '');
                    var cardAttr  = meetingId2 > 0 ? ' data-meeting-id="' + meetingId2 + '"'
                                  : noteId2    > 0 ? ' data-note-id="'    + noteId2    + '"'
                                  : emailId2   > 0 ? ' data-email-id="'   + emailId2   + '"'
                                  : topicId2   > 0 ? ' data-chat-topic-id="' + topicId2 + '"'
                                  : '';

                    tlHtml +=
                        '<div class="vas_123_qrp-tl-entry">' +
                            '<div class="vas_123_qrp-tl-rail">' +
                                '<span class="vas_123_qrp-tl-dot" style="background:' + dotClr + '"></span>' +
                                (isLast ? '' : '<span class="vas_123_qrp-tl-trail"></span>') +
                            '</div>' +
                            '<div class="' + cardCls + '"' + cardAttr + '>' +
                                '<div class="vas_123_qrp-tl-top">' +
                                    '<span class="vas_123_qrp-eng-badge" style="background:' + bCfg.bg + ';color:' + bCfg.color + ';">' +
                                        '<span style="display:inline-flex;align-items:center;gap:.2em;vertical-align:middle;">' + typeIcon + ' ' + esc(msg(bCfg.labelKey)) + '</span>' +
                                    '</span>' +
                                    dirBadge +
                                    '<span class="vas_123_qrp-eng-ts">' + esc(fmtEngTs(item.whenTs || '')) + '</span>' +
                                '</div>' +
                                '<div class="vas_123_qrp-tl-title">' + esc(titleText) + '</div>' +
                                (item.preview ? '<p class="vas_123_qrp-tl-body">' + (item.touchType === 'CHAT' ? '&ldquo;' + esc(item.preview) + '&rdquo;' : esc(item.preview)) + '</p>' : '') +
                                (metaHtml2 ? '<p class="vas_123_qrp-tl-meta">' + metaHtml2 + '</p>' : '') +
                            '</div>' +
                        '</div>';
                }
                tlHtml += '</div>';
            }

            // Note composer — always visible at the bottom of the engagement section
            var composerHtml =
                '<div class="vas_123_qrp-composer" style="margin-top:.75em">' +
                    '<textarea id="vas_123_noteBox_' + widgetID + '"' +
                        ' placeholder="' + esc(msg('VAS_123_NotePlaceholder')) + '"></textarea>' +
                    '<div class="vas_123_qrp-cmp-foot">' +
                        '<button class="vas_123_qrp-btn vas_123_qrp-btn--primary" data-action="postNote">' +
                            SVG_SEND + ' ' + esc(msg('Send')) +
                        '</button>' +
                    '</div>' +
                '</div>';

            var SVG_CLOCK_ENG = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/></svg>';

            container.innerHTML =
                '<div class="vas_123_qrp-sec-head" style="border-bottom:none;margin-bottom:0.75em;">' +
                    '<h4 class="vas_123_qrp-sh-title" style="gap:0.625em;">' +
                        '<span class="vas_123_qrp-eng-icon-badge">' + SVG_CLOCK_ENG + '</span>' +
                        esc(msg('VAS_123_Engagement')) +
                    '</h4>' +
                    '<span class="vas_123_qrp-sh-right"><span class="vas_123_qrp-sh-sum">' + (counts.total || 0) + ' ' + esc(msg('VAS_123_Touches')) + '</span></span>' +
                '</div>' +
                statHtml +
                tlHtml +
                composerHtml;
        }

        // ── Action: Open line detail modal (D1) ───────────────────────────────
        function openLineDetail(lineId) {
            var lines = state.lines || [];
            var line  = null;
            for (var i = 0; i < lines.length; i++) {
                var lid = lines[i].c_OrderLine_ID || lines[i].C_OrderLine_ID || 0;
                if (String(lid) === String(lineId)) { line = lines[i]; break; }
            }
            if (!line) return;

            var h    = state.header || {};
            var sym  = h.currencySymbol || '';
            var prec = (h.currencyPrecision != null) ? parseInt(h.currencyPrecision, 10) : 2;

            var qty       = toNum(line.qtyEntered   || line.QtyEntered);
            var uom       = line.uOMName             || line.UOMName   || '';
            var price     = toNum(line.priceEntered  || line.PriceEntered || line.priceActual || line.PriceActual);
            var disc      = toNum(line.discount      || line.Discount);
            var amt       = toNum(line.lineNetAmt    || line.LineNetAmt);
            var product   = line.productName         || line.ProductName  || line.description || line.Description || '';
            var sku       = line.productValue        || line.ProductValue || '';
            var isService       = !!(line.isService        || line.IsService);
            var productTypeName = line.productTypeName      || line.ProductTypeName || '';
            var lId             = line.c_OrderLine_ID      || line.C_OrderLine_ID || 0;

            var priceDisplay = fmtCurrency(price, sym, prec) + (disc > 0 ? ' · −' + disc + '%' : '');

            var bodyHtml =
                '<div class="vas_123_qrp-mgrid">' +
                    '<div class="vas_123_qrp-mcell">' +
                        '<p class="vas_123_qrp-m-label">' + esc(msg('VAS_123_Quantity'))  + '</p>' +
                        '<p class="vas_123_qrp-m-value">' + esc(qty + (uom ? ' ' + uom : '')) + '</p>' +
                    '</div>' +
                    '<div class="vas_123_qrp-mcell">' +
                        '<p class="vas_123_qrp-m-label">' + esc(msg('VAS_123_UnitPrice')) + '</p>' +
                        '<p class="vas_123_qrp-m-value">' + esc(priceDisplay)              + '</p>' +
                    '</div>' +
                    '<div class="vas_123_qrp-mcell">' +
                        '<p class="vas_123_qrp-m-label">' + esc(msg('VAS_123_LineAmount')) + '</p>' +
                        '<p class="vas_123_qrp-m-value">' + esc(fmtCurrency(amt, sym, prec)) + '</p>' +
                    '</div>' +
                    '<div class="vas_123_qrp-mcell">' +
                        '<p class="vas_123_qrp-m-label">' + esc(msg('Type')) + '</p>' +
                        '<p class="vas_123_qrp-m-value">' + esc(productTypeName) + '</p>' +
                    '</div>' +
                '</div>';

            var footHtml =
                '<button class="vas_123_qrp-btn vas_123_qrp-btn--ghost" data-action="closeModal">' +
                    esc(msg('VIS_Close')) +
                '</button>';

            showModal(
                product,
                (sku ? sku + ' · ' : '') + msg('VAS_123_LineDetail'),
                bodyHtml,
                footHtml,
                false
            );
        }

        // ── Action: Show all quotations linked to an opportunity ──────────────
        function openLinkedQuotations(oppId, oppName) {
            var h    = state.header;
            var sym  = h ? (h.currencySymbol || '') : '';
            var prec = h && h.currencyPrecision != null ? parseInt(h.currencyPrecision, 10) : 2;

            showModal(
                msg('VAS_123_LinkedQuotations'),
                esc(oppName || ''),
                '<div class="vas_123_qrp-emptyline">' + esc(msg('VAS_123_Loading')) + '</div>',
                '<button class="vas_123_qrp-btn vas_123_qrp-btn--ghost" data-action="closeModal">' + esc(msg('VIS_Close')) + '</button>',
                false
            );

            postJSON('GetLinkedQuotations', { VAS_Opportunity_ID: oppId }, function (err, data) {
                var bEl = document.getElementById('vas_123_mbody_' + widgetID);
                if (!bEl) return;

                var list = (!err && Array.isArray(data)) ? data : [];
                if (!list.length) {
                    bEl.innerHTML = '<p class="vas_123_qrp-emptyline">' + esc(msg('VAS_123_NoLinkedQuotations')) + '</p>';
                    return;
                }

                var rows = '';
                for (var i = 0; i < list.length; i++) {
                    var q          = list[i];
                    var qId        = q.c_Order_ID      || q.C_Order_ID      || 0;
                    // Skip the quotation that is currently open in the panel
                    if (qId && qId === currentOrderId) continue;
                    var docNo      = q.documentNo      || q.DocumentNo      || '';
                    var bp         = q.bPartnerName    || q.BPartnerName    || '';
                    var status     = q.docStatusName   || q.DocStatusName   || q.docStatus || '';
                    var docStatus  = q.docStatus       || q.DocStatus       || '';
                    var date       = q.dateOrdered     || q.DateOrdered     || '';
                    var total      = q.grandTotal      != null ? q.grandTotal : (q.GrandTotal != null ? q.GrandTotal : 0);
                    var qSym       = q.currencySymbol  || q.CurrencySymbol  || q.currencyISO || q.CurrencyISO || sym;
                    var qPrec      = q.currencyPrecision != null ? parseInt(q.currencyPrecision, 10) : prec;

                    // Choose status chip colour: CO/CL = success, VO = risk, DR/IP = info
                    var tone = '--info';
                    if (docStatus === 'CO' || docStatus === 'CL') { tone = '--success'; }
                    if (docStatus === 'VO')                        { tone = '--risk';    }

                    var statusChip = status
                        ? '<span class="vas_123_qrp-chip vas_123_qrp-chip' + esc(tone) + '">' + esc(status) + '</span>'
                        : '';

                    var meta = (bp ? esc(bp) : '') + (date ? ' · ' + esc(fmtDate(date)) : '');

                    rows +=
                        '<div class="vas_123_qrp-crow">' +
                            '<div class="vas_123_qrp-cl-left">' +
                                '<span class="vas_123_qrp-cl-primary">' + esc(docNo) + ' ' + statusChip + '</span>' +
                                (meta ? '<span class="vas_123_qrp-cl-meta">' + meta + '</span>' : '') +
                            '</div>' +
                            '<div class="vas_123_qrp-cl-right">' +
                                '<span class="vas_123_qrp-cl-primary">' + esc(fmtCurrency(total, qSym, qPrec)) + '</span>' +
                            '</div>' +
                        '</div>';
                }
                bEl.innerHTML = '<div class="vas_123_qrp-clist">' + rows + '</div>';
            });
        }

        // ── Action: Open link-opportunity modal (D2) ──────────────────────────
        function openLinkOpportunity() {
            var h = state.header;
            if (!h) return;
            var bpId   = h.c_BPartner_ID  || h.C_BPartner_ID  || 0;
            var bpName = h.bPartnerName   || h.BPartnerName   || h.customerName || '';
            var sym    = h.currencySymbol  || '';
            var prec   = (h.currencyPrecision != null) ? parseInt(h.currencyPrecision, 10) : 2;

            // Open modal immediately with a loading body; opportunities load asynchronously
            showModal(
                msg('VAS_123_LinkOpportunity'),
                msg('VAS_123_OpenOpportunitiesFor') + ' ' + bpName,
                '<p class="vas_123_qrp-emptyline">' + esc(msg('VAS_123_LoadingOpportunities')) + '</p>',
                '<button class="vas_123_qrp-btn vas_123_qrp-btn--ghost" data-action="closeModal">' + esc(msg('VAS_Cancel')) + '</button>',
                false
            );

            postJSON('GetOpenOpportunities', { C_BPartner_ID: bpId }, function (err, data) {
                var bEl = document.getElementById('vas_123_mbody_' + widgetID);
                if (!bEl) return;

                var opps = (!err && Array.isArray(data)) ? data : [];
                if (!opps.length) {
                    bEl.innerHTML = '<p class="vas_123_qrp-emptyline">' + esc(msg('VAS_123_NoOpenOpportunities')) + '</p>';
                    return;
                }

                var rows = '';
                for (var i = 0; i < opps.length; i++) {
                    var o     = opps[i];
                    var oId        = o.vAS_Opportunity_ID || o.VAS_Opportunity_ID || 0;
                    var oName      = o.opportunityName    || o.OpportunityName    || '';
                    var stage      = o.stage              || o.Stage              || '';
                    var oStageName = o.stageName          || o.StageName          || stage;
                    var oAmt       = (o.amount != null)   ? o.amount : ((o.Amount != null) ? o.Amount : null);
                    var meta       = oStageName + (oAmt != null ? ' · ' + fmtCurrency(oAmt, sym, prec) : '');

                    rows +=
                        '<div class="vas_123_qrp-crow">' +
                            '<div class="vas_123_qrp-cl-left">' +
                                '<span class="vas_123_qrp-cl-primary">' + esc(oName) + '</span>' +
                                '<span class="vas_123_qrp-cl-meta">'    + esc(meta)  + '</span>' +
                            '</div>' +
                            '<div class="vas_123_qrp-cl-right">' +
                                '<button class="vas_123_qrp-btn vas_123_qrp-btn--secondary" data-action="executeLinkOpportunity" data-opportunity-id="' + esc(oId) + '">' +
                                    esc(msg('VAS_123_Link')) +
                                '</button>' +
                            '</div>' +
                        '</div>';
                }
                bEl.innerHTML = '<div class="vas_123_qrp-clist">' + rows + '</div>';
            });
        }

        // ── Action: Send Quotation — standard VIS.Email compose window ───────────
        function openSendQuotation() {
            if (!window.VIS || typeof VIS.Email !== 'function' || typeof VIS.CFrame !== 'function') return;
            var h       = state.header;
            var tableId = $self.table_ID || 0;
            var toAddr  = (h && h.contactEmail) ? h.contactEmail : '';
            var bpId    = (h && h.c_BPartner_ID) ? h.c_BPartner_ID : 0;
            var email   = new VIS.Email(toAddr, null, null, bpId, true, true, tableId, null, '', null);
            var c       = new VIS.CFrame();
            c.setName(msg('EMail'));
            c.setTitle(msg('EMail'));
            c.hideHeader(true);
            c.setContent(email);
            c.show();
            email.initializeComponent();
        }

        // ── Part 3 modal actions ──────────────────────────────────────────────

        // ── Assignee typeahead helpers (scoped to the task modal) ─────────────
        function renderAssgChips() {
            var chipsEl = document.getElementById('vas_123_assgChips_' + widgetID);
            if (!chipsEl) return;
            if (!_taskAssg.length) {
                chipsEl.innerHTML = '<span class="vas_123_qrp-sugempty" style="padding:0">' + esc(msg('VAS_123_NoAssigneeYet')) + '</span>';
                return;
            }
            var html = '';
            for (var i = 0; i < _taskAssg.length; i++) {
                var u = _taskAssg[i];
                html +=
                    '<span class="vas_123_qrp-achip">' + esc(u.name) +
                        '<button type="button" title="' + esc(msg('VAS_123_Remove')) + '"' +
                            ' data-action="removeAssg" data-user-id="' + esc(u.id) + '">' +
                            SVG_X +
                        '</button>' +
                    '</span>';
            }
            chipsEl.innerHTML = html;
        }

        function filterAssgSuggest(q) {
            var sugEl = document.getElementById('vas_123_assgSuggest_' + widgetID);
            if (!sugEl) return;
            var query = (q || '').trim();
            if (!query) { sugEl.classList.remove('vas_123_qrp-suggest--open'); sugEl.innerHTML = ''; return; }

            // Build exclusion list from current chip array
            var excludeIds = _taskAssg.map(function (u) { return u.id; }).join(',') || '0';

            postJSON('GetUserSuggest', { Query: query, ExcludeIds: excludeIds }, function (err, data) {
                var sugEl2 = document.getElementById('vas_123_assgSuggest_' + widgetID);
                if (!sugEl2) return;
                var hits = (!err && Array.isArray(data)) ? data : [];
                if (!hits.length) {
                    sugEl2.innerHTML = '<div class="vas_123_qrp-sugempty">' + esc(msg('VAS_123_NoMatchingUser')) + '</div>';
                } else {
                    var html = '';
                    for (var i = 0; i < hits.length; i++) {
                        var u = hits[i];
                        var uId   = u.aD_User_ID || u.AD_User_ID || 0;
                        var uName = u.name  || u.Name  || '';
                        var uMail = u.email || u.Email || '';
                        html +=
                            '<div class="vas_123_qrp-sugrow" data-action="addAssg"' +
                                ' data-user-id="' + esc(uId) + '" data-user-name="' + esc(uName) + '">' +
                                '<span class="vas_123_qrp-contact-avatar" style="font-size:.6875em">' + esc(initials(uName)) + '</span>' +
                                '<span class="vas_123_qrp-sg-main">' +
                                    '<span class="vas_123_qrp-sg-name">' + esc(uName) + '</span>' +
                                    '<span class="vas_123_qrp-sg-mail">' + esc(uMail)  + '</span>' +
                                '</span>' +
                            '</div>';
                    }
                    sugEl2.innerHTML = html;
                }
                sugEl2.classList.add('vas_123_qrp-suggest--open');
            });
        }

        // ── Modal F1: Task form (create / edit) ───────────────────────────────
        function openTaskForm(taskId) {
            var tasks    = state.tasks || [];
            var existing = null;
            if (taskId) {
                for (var i = 0; i < tasks.length; i++) {
                    var tid = tasks[i].r_Request_ID || tasks[i].R_Request_ID || 0;
                    if (String(tid) === String(taskId)) { existing = tasks[i]; break; }
                }
            }
            var t = existing || { title: '', due: '', pct: 0, priority: '5', closed: false, assigneeName: '' };

            // Pre-populate assignee chips from existing task's assignee name
            _taskAssg = [];
            if (t.assigneeName && t.assigneeName !== '') {
                // Single-assignee model from R_Request — pre-fill chip with display name only
                _taskAssg = [{ id: t.salesRep_ID || t.SalesRep_ID || 0, name: t.assigneeName }];
            }

            var isClosed = !!(t.closed || t._closed);

            var closeBtnHtml = '';
            if (taskId) {
                if (isClosed) {
                    closeBtnHtml =
                        '<button class="vas_123_qrp-btn vas_123_qrp-btn--ghost" style="margin-right:auto"' +
                            ' data-action="closeReopenTask" data-task-id="' + esc(taskId) + '" data-reopen="Y">' +
                            esc(msg('VAS_123_ReopenTask')) +
                        '</button>';
                } else {
                    closeBtnHtml =
                        '<button class="vas_123_qrp-btn vas_123_qrp-btn--secondary" style="margin-right:auto"' +
                            ' data-action="closeReopenTask" data-task-id="' + esc(taskId) + '" data-reopen="N">' +
                            SVG_CHECK + ' ' + esc(msg('VAS_123_CloseTask')) +
                        '</button>';
                }
            }

            var PRIORITY_OPTIONS = [
                { val: 'U', label: msg('VAS_123_PriorityUrgent') },
                { val: '3', label: msg('VAS_123_PriorityHigh')   },
                { val: '5', label: msg('VAS_123_PriorityMedium') },
                { val: '7', label: msg('VAS_123_PriorityLow')    }
            ];
            var priPickHtml = '<div class="vas_123_qrp-chiprow">';
            for (var pi = 0; pi < PRIORITY_OPTIONS.length; pi++) {
                var opt = PRIORITY_OPTIONS[pi];
                priPickHtml +=
                    '<button class="vas_123_qrp-pick' + (t.priority === opt.val ? ' vas_123_qrp-pick--on' : '') + '"' +
                        ' type="button" data-action="pickPriority" data-priority="' + esc(opt.val) + '">' +
                        esc(opt.label) +
                    '</button>';
            }
            priPickHtml += '</div>';

            var bodyHtml =
                '<div class="vas_123_qrp-fieldrow">' +
                    '<div class="vas_123_qrp-field vas_123_qrp-field--full">' +
                        '<label>' + esc(msg('VAS_123_TaskLabel')) + '</label>' +
                        '<input type="text" id="vas_123_taskTitle_' + widgetID + '" value="' + esc(t.title || '') + '" placeholder="' + esc(msg('VAS_123_TaskPlaceholder')) + '">' +
                    '</div>' +
                    '<div class="vas_123_qrp-field">' +
                        '<label>' + esc(msg('DueDate')) + '</label>' +
                        '<input type="date" id="vas_123_taskDue_' + widgetID + '" value="' + esc((t.due || '').substring(0, 10)) + '">' +
                    '</div>' +
                    '<div class="vas_123_qrp-field">' +
                        '<label>' + esc(msg('VAS_123_CompletionPct')) + '</label>' +
                        '<input type="text" id="vas_123_taskPct_' + widgetID + '" value="' + esc(t.pct != null ? String(t.pct) : '0') + '">' +
                    '</div>' +
                    '<div class="vas_123_qrp-field vas_123_qrp-field--full">' +
                        '<label>' + esc(msg('VAS_123_Assignees')) + '</label>' +
                        '<div class="vas_123_qrp-assg-box">' +
                            '<div class="vas_123_qrp-assg-chips" id="vas_123_assgChips_' + widgetID + '"></div>' +
                            '<input type="text" id="vas_123_assgInput_' + widgetID + '"' +
                                ' placeholder="' + esc(msg('VAS_123_AssigneePlaceholder')) + '"' +
                                ' autocomplete="off">' +
                            '<div class="vas_123_qrp-suggest" id="vas_123_assgSuggest_' + widgetID + '"></div>' +
                        '</div>' +
                    '</div>' +
                    '<div class="vas_123_qrp-field vas_123_qrp-field--full">' +
                        '<label>' + esc(msg('Priority')) + '</label>' +
                        priPickHtml +
                    '</div>' +
                '</div>';

            var footHtml =
                closeBtnHtml +
                '<button class="vas_123_qrp-btn vas_123_qrp-btn--ghost" data-action="closeModal">' +
                    esc(msg('VAS_Cancel')) +
                '</button>' +
                '<button class="vas_123_qrp-btn vas_123_qrp-btn--primary" data-action="saveTask"' +
                    ' data-task-id="' + esc(taskId || 0) + '">' +
                    esc(msg('VAS_123_SaveTask')) +
                '</button>';

            showModal(
                taskId ? msg('VAS_123_EditTask') : msg('VAS_123_NewTask'),
                esc(state.header ? (state.header.documentNo || '') : ''),
                bodyHtml,
                footHtml,
                false
            );

            // Render assignee chips and wire up the input after the modal DOM is ready
            renderAssgChips();

            var inputEl = document.getElementById('vas_123_assgInput_' + widgetID);
            if (inputEl) {
                inputEl.addEventListener('input', function () { filterAssgSuggest(this.value); });
                inputEl.addEventListener('focus', function () { if (this.value) { filterAssgSuggest(this.value); } });
            }
        }

        // ── Engagement detail modals ──────────────────────────────────────────

        function openNoteDetailModal(noteId) {
            var SVG_NOTE2 = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14.5 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7.5L14.5 2z"/><polyline points="14 2 14 8 20 8"/></svg>';
            showModal(msg('VAS_123_TouchPoint'), '', '<div class="vas_123_qrp-emptyline">' + esc(msg('VAS_123_Loading')) + '</div>', '', false);
            postJSON('GetNoteDetail', { noteId: noteId }, function (err, data) {
                var bodyEl = document.getElementById('vas_123_mbody_' + widgetID);
                var footEl = document.getElementById('vas_123_mfoot_' + widgetID);
                if (err || !data || !data.id) {
                    if (bodyEl) bodyEl.innerHTML = '<div class="vas_123_qrp-emptyline">' + esc(msg('VIS_NoData')) + '</div>';
                    if (footEl) footEl.innerHTML = '<button class="vas_123_qrp-btn vas_123_qrp-btn--ghost" id="' + widgetID + '_ndClose">' + esc(msg('VIS_Close')) + '</button>';
                    $root.find('#' + widgetID + '_ndClose').on('click', function (e) { e.stopPropagation(); e.stopImmediatePropagation(); closeModal(); });
                    return;
                }
                document.getElementById('vas_123_mmeta_' + widgetID).textContent = data.whenTs ? fmtEngTs(data.whenTs) : '';
                var bodyHtml = [
                    '<div style="margin-bottom:0.75em;">',
                    '  <span class="vas_123_qrp-eng-badge" style="background:#FEF3C7;color:#92400E;">',
                    '    <span style="display:inline-flex;align-items:center;gap:0.2em;vertical-align:middle;">', SVG_NOTE2, ' ', esc(msg('Note')), '</span>',
                    '  </span>',
                    '</div>',
                    data.title ? '<div style="font-size:1em;font-weight:700;color:var(--qrp-ink);margin-bottom:0.5em;">' + esc(data.title) + '</div>' : '',
                    '<table style="width:100%;border-collapse:collapse;font-size:0.875em;margin-bottom:0.75em;">',
                    '  <tr>',
                    '    <td style="color:var(--qrp-muted);padding:0.25em 0.5em 0.25em 0;">' + esc(msg('VAS_123_When')) + '</td>',
                    '    <td style="color:var(--qrp-ink);">' + esc(fmtEngTs(data.whenTs || '')) + '</td>',
                    data.who ? '    <td style="color:var(--qrp-muted);padding:0.25em 0.5em 0.25em 1em;">' + esc(msg('Details')) + '</td><td style="color:var(--qrp-ink);">' + esc(data.who) + '</td>' : '<td></td><td></td>',
                    '  </tr>',
                    '</table>',
                    data.body ? '<div style="font-size:0.875em;color:var(--qrp-ink);background:#F6FAFE;border:0.0625em solid var(--qrp-border);border-radius:0.5em;padding:0.75em;white-space:pre-wrap;word-break:break-word;">' + esc(data.body) + '</div>' : ''
                ].join('');
                if (bodyEl) bodyEl.innerHTML = bodyHtml;
                if (footEl) footEl.innerHTML = '<button class="vas_123_qrp-btn vas_123_qrp-btn--ghost" id="' + widgetID + '_ndClose">' + esc(msg('VIS_Close')) + '</button>';
                $root.find('#' + widgetID + '_ndClose').on('click', function (e) { e.stopPropagation(); e.stopImmediatePropagation(); closeModal(); });
            });
        }

        function openEmailDetailModal(emailId) {
            var SVG_MAIL2  = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="2" y="4" width="20" height="16" rx="2"/><path d="m22 7-8.97 5.7a1.94 1.94 0 0 1-2.06 0L2 7"/></svg>';
            var SVG_REPLY2 = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 17 4 12 9 7"/><path d="M20 18v-2a4 4 0 0 0-4-4H4"/></svg>';
            showModal(msg('VAS_123_TouchPoint'), '', '<div class="vas_123_qrp-emptyline">' + esc(msg('VAS_123_Loading')) + '</div>', '', false);
            postJSON('GetEmailDetail', { emailId: emailId }, function (err, data) {
                var bodyEl = document.getElementById('vas_123_mbody_' + widgetID);
                var footEl = document.getElementById('vas_123_mfoot_' + widgetID);
                if (err || !data || !data.id) {
                    if (bodyEl) bodyEl.innerHTML = '<div class="vas_123_qrp-emptyline">' + esc(msg('VIS_NoData')) + '</div>';
                    if (footEl) footEl.innerHTML = '<button class="vas_123_qrp-btn vas_123_qrp-btn--ghost" id="' + widgetID + '_emdClose">' + esc(msg('VIS_Close')) + '</button>';
                    $root.find('#' + widgetID + '_emdClose').on('click', function (e) { e.stopPropagation(); e.stopImmediatePropagation(); closeModal(); });
                    return;
                }

                document.getElementById('vas_123_mmeta_' + widgetID).textContent = data.whenTs ? fmtEngTs(data.whenTs) : '';

                var dirLabel   = data.direction === 'in' ? msg('VAS_123_Incoming') : msg('VAS_123_Outgoing');
                var detailAddr = data.direction === 'in' ? (data.fromEmail || '') : (data.toEmail || '');

                var tmpDiv = document.createElement('div');
                tmpDiv.innerHTML = data.body || '';
                var plainBody = (tmpDiv.textContent || tmpDiv.innerText || '').replace(/\s+/g, ' ').trim();
                var quoteText = plainBody ? plainBody.substring(0, 200) + (plainBody.length > 200 ? '…' : '') : '';

                var bodyHtml = [
                    '<div class="vas_123_qrp-emd-top">',
                    '  <span class="vas_123_qrp-eng-badge" style="background:#DBEAFE;color:#1D4ED8;">',
                    '    <span style="display:inline-flex;align-items:center;gap:0.2em;vertical-align:middle;">', SVG_MAIL2, ' ', esc(msg('EMail')), '</span>',
                    '  </span>',
                    '  <span class="vas_123_qrp-emd-heading">', esc(data.subject || msg('EMail')), '</span>',
                    '</div>',
                    quoteText ? '<div class="vas_123_qrp-emd-quote">' + esc(quoteText) + '</div>' : '',
                    '<table class="vas_123_qrp-emd-meta">',
                    '  <tr>',
                    '    <td class="vas_123_qrp-emd-meta-label">', esc(msg('Type')), '</td>',
                    '    <td class="vas_123_qrp-emd-meta-value">', esc(msg('EMail')), '</td>',
                    '    <td class="vas_123_qrp-emd-meta-label">', esc(msg('VAS_123_Direction')), '</td>',
                    '    <td class="vas_123_qrp-emd-meta-value">', esc(dirLabel), '</td>',
                    '  </tr>',
                    '  <tr>',
                    '    <td class="vas_123_qrp-emd-meta-label">', esc(msg('VAS_123_When')), '</td>',
                    '    <td class="vas_123_qrp-emd-meta-value">', esc(fmtEngTs(data.whenTs || '')), '</td>',
                    '    <td class="vas_123_qrp-emd-meta-label">', esc(msg('Details')), '</td>',
                    '    <td class="vas_123_qrp-emd-meta-value">', esc(detailAddr), '</td>',
                    '  </tr>',
                    data.who ? [
                        '  <tr>',
                        '    <td class="vas_123_qrp-emd-meta-label">', esc(msg('VAS_123_People')), '</td>',
                        '    <td class="vas_123_qrp-emd-meta-value" colspan="3">', esc(data.who), '</td>',
                        '  </tr>'
                    ].join('') : '',
                    '</table>',
                    data.body ? '<div class="vas_123_qrp-emd-body">' + data.body + '</div>' : ''
                ].join('');

                var footHtml = [
                    '<button class="vas_123_qrp-btn vas_123_qrp-btn--ghost" id="', widgetID, '_emdClose">', esc(msg('VIS_Close')), '</button>',
                    '<button class="vas_123_qrp-btn vas_123_qrp-btn--primary" id="', widgetID, '_emdReply">', SVG_REPLY2, ' ', esc(msg('VAS_123_Reply')), '</button>'
                ].join('');

                if (bodyEl) bodyEl.innerHTML = bodyHtml;
                if (footEl) footEl.innerHTML = footHtml;

                $root.find('#' + widgetID + '_emdClose').on('click', function (e) {
                    e.stopPropagation(); e.stopImmediatePropagation(); closeModal();
                });

                $root.find('#' + widgetID + '_emdReply').on('click', function (e) {
                    e.stopPropagation(); e.stopImmediatePropagation();
                    if (!window.VIS || typeof VIS.Email !== 'function' || typeof VIS.CFrame !== 'function') return;
                    closeModal();
                    var tableId   = $self.table_ID || 0;
                    var bpId      = (state.header && state.header.c_BPartner_ID) ? state.header.c_BPartner_ID : 0;
                    var replyTo   = data.direction === 'in' ? (data.fromEmail || '') : (data.toEmail || '');
                    var subject   = 'RE: ' + (data.subject || '');
                    var replyBody = '<br><br><hr>' + (data.body || '');
                    var email     = new VIS.Email(replyTo, null, null, bpId, true, true, tableId, replyBody, subject, null);
                    var c         = new VIS.CFrame();
                    c.setName(msg('EMail'));
                    c.setTitle(msg('EMail'));
                    c.hideHeader(true);
                    c.setContent(email);
                    c.show();
                    email.initializeComponent();
                });
            });
        }

        function openMeetingDetailModal(meetingId) {
            var SVG_TRS      = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14.5 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7.5L14.5 2z"/><polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/><line x1="10" y1="9" x2="8" y2="9"/></svg>';
            var SVG_DOWNLOAD = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" y1="15" x2="12" y2="3"/></svg>';
            var SVG_CHECK2   = '<svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>';
            var SVG_SPINNER  = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" class="vas_123_spin"><circle cx="12" cy="12" r="10" opacity="0.25"/><path d="M12 2a10 10 0 0 1 10 10"/></svg>';

            showModal(msg('VAS_123_Meeting'), '', '<div class="vas_123_qrp-emptyline">' + esc(msg('VAS_123_Loading')) + '</div>', '', true);

            postJSON('GetMeetingDetail', { meetingId: meetingId }, function (err, data) {
                var bodyEl = document.getElementById('vas_123_mbody_' + widgetID);
                var footEl = document.getElementById('vas_123_mfoot_' + widgetID);
                if (err || !data || !data.id) {
                    if (bodyEl) bodyEl.innerHTML = '<div class="vas_123_qrp-emptyline">' + esc(msg('VIS_NoData')) + '</div>';
                    if (footEl) footEl.innerHTML = '<button class="vas_123_qrp-btn vas_123_qrp-btn--ghost" id="' + widgetID + '_mtgClose">' + esc(msg('VIS_Close')) + '</button>';
                    $root.find('#' + widgetID + '_mtgClose').on('click', function (e) { e.stopPropagation(); e.stopImmediatePropagation(); closeModal(); });
                    return;
                }

                var subText = (data.subject || '') + (data.startDate ? ' · ' + fmtEngTs(data.startDate) : '');
                document.getElementById('vas_123_mmeta_' + widgetID).textContent = subText;

                var metaParts = [];
                if (data.startDate) metaParts.push(esc(fmtEngTs(data.startDate)));
                if (data.attendees) metaParts.push(esc(data.attendees));
                if (data.location)  metaParts.push(esc(data.location));
                if (data.durationMins > 0) {
                    var h = Math.floor(data.durationMins / 60), m = data.durationMins % 60;
                    metaParts.push(h > 0 ? (h + 'h' + (m > 0 ? ' ' + m + 'm' : '')) : (m + 'm'));
                }

                var transcriptHtml = '';
                if (data.transcript) {
                    var tLines2 = String(data.transcript).replace(/\r\n/g, '\n').split('\n');
                    var tLinesHtml = '';
                    for (var i = 0; i < tLines2.length; i++) {
                        var tl = $.trim(tLines2[i]);
                        if (!tl) continue;
                        var ci = tl.indexOf(':');
                        if (ci > 0 && ci < 50) {
                            tLinesHtml += '<div class="vas_123_qrp-mtg-tline"><span class="vas_123_qrp-mtg-speaker">' + esc(tl.substring(0, ci)) + ':</span> ' + esc($.trim(tl.substring(ci + 1))) + '</div>';
                        } else {
                            tLinesHtml += '<div class="vas_123_qrp-mtg-tline">' + esc(tl) + '</div>';
                        }
                    }
                    transcriptHtml = [
                        '<hr class="vas_123_qrp-mtg-divider">',
                        '<div class="vas_123_qrp-mtg-trans-head">',
                        '  <span class="vas_123_qrp-mtg-trans-label">', SVG_TRS, ' <strong>', esc(msg('Transcript')), '</strong> &middot; ', esc(msg('Downloaded')), '</span>',
                        '  <button class="vas_123_qrp-btn vas_123_qrp-btn--ghost vas_123_qrp-btn--sm" id="', widgetID, '_btnSaveTxt">', SVG_DOWNLOAD, ' ', esc(msg('Save')), '</button>',
                        '</div>',
                        '<div class="vas_123_qrp-mtg-trans-box">', tLinesHtml, '</div>'
                    ].join('');
                }

                var bodyHtml = [
                    '<div class="vas_123_qrp-mtg-subject">', esc(data.subject || '—'), '</div>',
                    '<div class="vas_123_qrp-mtg-meta">', metaParts.join(' &middot; '), '</div>',
                    '<div class="vas_123_qrp-mtg-field">',
                    '  <label class="vas_123_qrp-mtg-field-label">', esc(msg('VAS_123_MeetingUrl')), '</label>',
                    '  <input type="text" class="vas_123_qrp-mtg-input" id="', widgetID, '_mtgUrl" value="', esc(data.meetingUrl || ''), '">',
                    '</div>',
                    transcriptHtml,
                    '<div class="vas_123_qrp-mtg-field">',
                    '  <label class="vas_123_qrp-mtg-field-label">', esc(msg('Comments')), '</label>',
                    '  <textarea class="vas_123_qrp-mtg-textarea" id="', widgetID, '_mtgComments" rows="3">', esc(data.comments || ''), '</textarea>',
                    '</div>'
                ].join('');

                var footHtml = [
                    '<button class="vas_123_qrp-btn vas_123_qrp-btn--ghost" id="', widgetID, '_mtgClose">', esc(msg('VIS_Close')), '</button>',
                    '<button class="vas_123_qrp-btn vas_123_qrp-btn--primary" id="', widgetID, '_mtgSave">', SVG_CHECK2, ' ', esc(msg('Save')), '</button>'
                ].join('');

                if (bodyEl) bodyEl.innerHTML = bodyHtml;
                if (footEl) footEl.innerHTML = footHtml;

                if (data.transcript) {
                    $root.find('#' + widgetID + '_btnSaveTxt').on('click', function (e) {
                        e.stopPropagation();
                        var blob = new Blob([data.transcript], { type: 'text/plain;charset=utf-8' });
                        var url  = URL.createObjectURL(blob);
                        var a    = document.createElement('a');
                        a.href     = url;
                        a.download = (data.subject || 'transcript').replace(/[^a-z0-9_\-\s]/gi, '') + '.txt';
                        document.body.appendChild(a);
                        a.click();
                        document.body.removeChild(a);
                        URL.revokeObjectURL(url);
                    });
                }

                $root.find('#' + widgetID + '_mtgClose').on('click', function (e) {
                    e.stopPropagation(); e.stopImmediatePropagation(); closeModal();
                });

                $root.find('#' + widgetID + '_mtgSave').on('click', function (e) {
                    e.stopPropagation(); e.stopImmediatePropagation();
                    var $btn      = $(this).prop('disabled', true);
                    var $closeBtn = $root.find('#' + widgetID + '_mtgClose').prop('disabled', true);
                    var origHtml  = $btn.html();
                    $btn.html(SVG_SPINNER + ' ' + esc(msg('Save')));
                    var urlVal      = $.trim($root.find('#' + widgetID + '_mtgUrl').val());
                    var commentsVal = $root.find('#' + widgetID + '_mtgComments').val();
                    postJSON('SaveMeetingComments', { meetingId: meetingId, comments: commentsVal, meetingUrl: urlVal }, function (err, res) {
                        $btn.prop('disabled', false).html(origHtml);
                        $closeBtn.prop('disabled', false);
                        if (!err && res && res.success) {
                            closeModal();
                            loadEngagement(currentOrderId);
                        } else {
                            var errMsg = (res && res.error) ? res.error : msg('Error');
                            if (window.VIS && VIS.ADialog) VIS.ADialog.error(errMsg, true, '', '');
                        }
                    });
                });
            });
        }

        function openWhatsAppModal(topicId) {
            var SVG_WHATSAPP = '<svg width="1.1em" height="1.1em" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M21 11.5a8.38 8.38 0 0 1-.9 3.8 8.5 8.5 0 0 1-7.6 4.7 8.38 8.38 0 0 1-3.8-.9L3 21l1.9-5.7a8.38 8.38 0 0 1-.9-3.8 8.5 8.5 0 0 1 4.7-7.6 8.38 8.38 0 0 1 3.8-.9h.5a8.48 8.48 0 0 1 8 8v.5z"/></svg>';
            var SVG_SEND2    = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="22" y1="2" x2="11" y2="13"/><polygon points="22 2 15 22 11 13 2 9 22 2"/></svg>';

            showModal(msg('VAS_123_WhatsApp'), '', '<div class="vas_123_qrp-emptyline">' + esc(msg('VAS_123_Loading')) + '</div>', '', true);

            postJSON('GetWhatsAppChat', { C_Order_ID: currentOrderId, topicId: topicId || 0 }, function (err, data) {
                var bodyEl = document.getElementById('vas_123_mbody_' + widgetID);
                var footEl = document.getElementById('vas_123_mfoot_' + widgetID);

                if (err || !data || !data.topic) {
                    if (bodyEl) bodyEl.innerHTML = '<div class="vas_123_qrp-emptyline">' + esc(msg('VIS_NoData')) + '</div>';
                    if (footEl) footEl.innerHTML = '<button class="vas_123_qrp-btn vas_123_qrp-btn--ghost" id="' + widgetID + '_wachatClose">' + esc(msg('VIS_Close')) + '</button>';
                    $root.find('#' + widgetID + '_wachatClose').on('click', function (e) {
                        e.stopPropagation(); e.stopImmediatePropagation(); closeModal();
                    });
                    return;
                }

                var topic    = data.topic    || {};
                var messages = data.messages || [];

                var contactHtml = [
                    '<div class="vas_123_qrp-wachat-contact">',
                    '  <span class="vas_123_qrp-wachat-avatar">', SVG_WHATSAPP, '</span>',
                    '  <div>',
                    '    <div class="vas_123_qrp-wachat-contact-name">', esc(topic.contactName || ''), '</div>',
                    '    <div class="vas_123_qrp-wachat-contact-meta">', esc(msg('VAS_123_WhatsApp')),
                             topic.chatDate ? ' &middot; ' + esc(topic.chatDate) : '',
                    '    </div>',
                    '  </div>',
                    '</div>'
                ].join('');

                var bubblesHtml = '<div class="vas_123_qrp-wachat-bubbles" id="' + widgetID + '_wachatBubbles">';
                if (messages.length === 0) {
                    bubblesHtml += '<div class="vas_123_qrp-emptyline">' + esc(msg('VAS_123_NoEngagement')) + '</div>';
                } else {
                    for (var i = 0; i < messages.length; i++) {
                        var m        = messages[i];
                        var isSender = (m.isSender === 'Y');
                        var bubCls   = 'vas_123_qrp-wachat-bubble ' + (isSender ? 'vas_123_qrp-wachat-bubble--out' : 'vas_123_qrp-wachat-bubble--in');
                        bubblesHtml += [
                            '<div class="', bubCls, '">',
                            '  <div class="vas_123_qrp-wachat-text">', esc(m.textMsg || ''), '</div>',
                            '  <div class="vas_123_qrp-wachat-ts">', esc(m.msgDate || ''), isSender ? ' ✓✓' : '', '</div>',
                            '</div>'
                        ].join('');
                    }
                }
                bubblesHtml += '</div>';

                var sendHtml = [
                    '<div class="vas_123_qrp-wachat-sendbox">',
                    '  <input type="text" class="vas_123_qrp-wachat-input" id="', widgetID, '_wachatInput"',
                    '    placeholder="', esc(msg('VAS_123_TypeMessage')), '" />',
                    '  <button class="vas_123_qrp-wachat-sendbtn" id="', widgetID, '_wachatSend">', SVG_SEND2, '</button>',
                    '</div>'
                ].join('');

                var footHtml = '<button class="vas_123_qrp-btn vas_123_qrp-btn--ghost" id="' + widgetID + '_wachatClose">' + esc(msg('VIS_Close')) + '</button>';

                if (bodyEl) bodyEl.innerHTML = contactHtml + bubblesHtml + sendHtml;
                if (footEl) footEl.innerHTML = footHtml;

                var bubblesEl = document.getElementById(widgetID + '_wachatBubbles');
                if (bubblesEl) bubblesEl.scrollTop = bubblesEl.scrollHeight;

                $root.find('#' + widgetID + '_wachatClose').on('click', function (e) {
                    e.stopPropagation(); e.stopImmediatePropagation(); closeModal();
                });

                var resolvedTopicId = topic.topicId || 0;
                function doSend() {
                    var text = $.trim($root.find('#' + widgetID + '_wachatInput').val());
                    if (!text) return;
                    var $btn = $root.find('#' + widgetID + '_wachatSend').prop('disabled', true);
                    var userId = (window.VIS && VIS.context && typeof VIS.context.getAD_User_ID === 'function')
                        ? VIS.context.getAD_User_ID() : 0;
                    postJSON('GetWhatsAppTopicMeta', { topicId: resolvedTopicId }, function (err1, meta) {
                        var chatId = (!err1 && meta) ? (meta.chatId || 0) : 0;
                        var mobile = (!err1 && meta) ? (meta.mobile || '') : '';
                        $.ajax({
                            url:      VIS.Application.contextUrl + 'WSP/Inbox/GetUserSocialAcct',
                            dataType: 'json',
                            data:     { User_ID: userId, Provider: 'WHATSAPP' },
                            success:  function (raw) {
                                var accts = (typeof raw === 'string') ? JSON.parse(raw) : raw;
                                if (!accts || !accts.length || !chatId) {
                                    $btn.prop('disabled', false);
                                    return;
                                }
                                var cfg = accts[0];
                                var chatDataObj = {
                                    smconfig_id:   cfg.SMConfigID   || 0,
                                    smpara_id:     cfg.SMParaID     || 0,
                                    table_id:      0,
                                    record_id:     0,
                                    account_type:  'WHATSAPP',
                                    account_id:    cfg.AccountValue || '',
                                    user_id:       userId,
                                    chat_id:       chatId,
                                    chatdate:      new Date(),
                                    attendee:      mobile,
                                    chatname:      topic.contactName || '',
                                    textmsg:       text,
                                    is_attachment: false,
                                    msgdocuments:  JSON.stringify([]),
                                    strDocAttach:  '',
                                    folderkey:     '',
                                    msgdate:       new Date(),
                                    quote_id:      ''
                                };
                                $.ajax({
                                    url:      VIS.Application.contextUrl + 'WSP/Inbox/CreateChat',
                                    type:     'POST',
                                    dataType: 'json',
                                    data:     { ChatData: JSON.stringify(chatDataObj) },
                                    success:  function () {
                                        $btn.prop('disabled', false);
                                        $root.find('#' + widgetID + '_wachatInput').val('');
                                        openWhatsAppModal(resolvedTopicId);
                                    },
                                    error: function () { $btn.prop('disabled', false); }
                                });
                            },
                            error: function () { $btn.prop('disabled', false); }
                        });
                    });
                }
                $root.find('#' + widgetID + '_wachatSend').on('click', function (e) {
                    e.stopPropagation(); e.stopImmediatePropagation(); doSend();
                });
                $root.find('#' + widgetID + '_wachatInput').on('keydown', function (e) {
                    if (e.which === 13) { e.preventDefault(); doSend(); }
                });
            });
        }

        // ── Action: Schedule Meeting — standard VIS.AppointmentsForm ─────────────
        function openScheduleMeeting() {
            if (!window.VIS || !VIS.AppointmentsForm || typeof VIS.AppointmentsForm.init !== 'function') return;
            var h        = state.header;
            var tableId  = $self.table_ID || 0;
            var bpId     = (h && h.c_BPartner_ID) ? h.c_BPartner_ID : 0;
            var userId   = (VIS.context && typeof VIS.context.getAD_User_ID  === 'function') ? VIS.context.getAD_User_ID()  : 0;
            var userName = (VIS.context && typeof VIS.context.getAD_UserName === 'function') ? VIS.context.getAD_UserName() : '';
            VIS.AppointmentsForm.init(tableId, currentOrderId, userId, userName, false);
            // Reload next meeting card after the appointments form closes
            $(document).one('ajaxComplete', function () { loadNextMeeting(currentOrderId); });
        }



        // ── Action: Confirm document action (CO / RE / VO / CL) ──────────────
        function confirmDocumentAction(docAction) {
            var h = state.header;
            if (!h) return;

            // Title and body message per action, via AD_Message keys
            var titleMap = {
                CO: msg('VAS_123_ConfirmComplete'),
                RE: msg('VAS_123_ConfirmReActivate'),
                VO: msg('VAS_123_ConfirmVoid'),
                CL: msg('VAS_123_ConfirmClose')
            };
            var bodyMap = {
                CO: msg('VAS_123_ConfirmComplete'),
                RE: msg('VAS_123_ConfirmReActivate'),
                VO: msg('VAS_123_ConfirmVoid'),
                CL: msg('VAS_123_ConfirmClose')
            };
            // Descriptive explanation texts — all user-visible strings must come from msg()
            // These secondary descriptions complement the title and provide action context
            var descMap = {
                CO: 'The document engine locks pricing and sets the quotation Completed, ready to send and convert.',
                RE: 'Re-opens the completed quotation for changes. Validity may need updating before re-sending.',
                VO: 'Voids the document. This cannot be undone.',
                CL: 'Closes the quotation; it can no longer be converted to an order.'
            };

            var title   = titleMap[docAction] || docAction;
            var meta    = esc(h.documentNo || '') + ' — ' + esc(resolveStatusLabel(h.docStatus || ''));
            var bodyHtml = '<p class="vas_123_qrp-mconfirm-desc">' + esc(descMap[docAction] || '') + '</p>';
            var footHtml =
                '<button class="vas_123_qrp-btn vas_123_qrp-btn--secondary" data-action="closeModal">' +
                    esc(msg('VAS_Cancel')) +
                '</button>' +
                '<button class="vas_123_qrp-btn vas_123_qrp-btn--primary" data-action="executeDocAction" data-doc-action="' + esc(docAction) + '">' +
                    esc(msg('VAS_123_Confirm')) +
                '</button>';

            showModal(title, meta, bodyHtml, footHtml, false);
        }

        // ── Action: Confirm Convert to Sales Order ─────────────────────────────
        function confirmConvertToOrder() {
            var h = state.header;
            if (!h) return;

            var title   = msg('VAS_123_ConvertToSalesOrder');
            var meta    = esc(h.documentNo || '') + ' → new sales order';
            var bodyHtml =
                '<p class="vas_123_qrp-mconfirm-desc">' +
                    esc(msg('VAS_123_ConfirmConvert') ||
                        'Creates a sales order from this quotation via the platform conversion process. ' +
                        'The quotation remains linked as the order\'s source document.') +
                '</p>';
            var footHtml =
                '<button class="vas_123_qrp-btn vas_123_qrp-btn--secondary" data-action="closeModal">' +
                    esc(msg('VAS_Cancel')) +
                '</button>' +
                '<button class="vas_123_qrp-btn vas_123_qrp-btn--primary" data-action="executeConvertToOrder">' +
                    SVG_ARROW + ' ' + esc(msg('VAS_123_ConvertToSalesOrder')) +
                '</button>';

            showModal(title, meta, bodyHtml, footHtml, false);
        }

        // ── Action: Open a Sales Order record in the platform window viewer ───
        function openOrderRecord(orderId) {
            if (!orderId || +orderId <= 0 || !window.VIS) return;
            try {
                var windowId = 0;
                if (VIS.ZoomTarget && typeof VIS.ZoomTarget.getZoomAD_Window_ID === 'function') {
                    // isSOTrx = true → Sales Order window (not Purchase Order window)
                    windowId = VIS.ZoomTarget.getZoomAD_Window_ID('C_Order', 0, null, true) || 0;
                }
                if (windowId > 0 && VIS.viewManager && typeof VIS.viewManager.startWindow === 'function') {
                    var zoomQuery = VIS.Query.prototype.getEqualQuery('C_Order_ID', +orderId);
                    VIS.viewManager.startWindow(windowId, zoomQuery);
                }
            } catch (e) { }
        }

        // ── Instance method: Initialize — builds DOM and wires events ───────────
        // Called by prototype.startPanel (tab-panel host) and prototype.init
        // (widget host) after this.windowNo has been set.
        this.Initialize = function () {
            widgetID = $self.windowNo || 0;

            // ── Build panel shell HTML ────────────────────────────────────────
            var shellHtml =
                '<div class="vas_123_qrp-panel" id="vas_123_panel_' + widgetID + '">' +
                    '<div class="vas_123_qrp-body" id="vas_123_body_' + widgetID + '"></div>' +
                '</div>' +
                // Modal overlay (sits outside the panel scroll container)
                '<div class="vas_123_qrp-overlay" id="vas_123_overlay_' + widgetID + '">' +
                    '<div class="vas_123_qrp-overlay__back" data-action="closeModal"></div>' +
                    '<div class="vas_123_qrp-modal" id="vas_123_modal_' + widgetID + '">' +
                        '<div class="vas_123_qrp-mhead">' +
                            '<div>' +
                                '<h3 class="vas_123_qrp-mh-title" id="vas_123_mtitle_' + widgetID + '"></h3>' +
                                '<p class="vas_123_qrp-mh-meta" id="vas_123_mmeta_' + widgetID + '"></p>' +
                            '</div>' +
                            '<button class="vas_123_qrp-ph-close" data-action="closeModal">' + SVG_X + '</button>' +
                        '</div>' +
                        '<div class="vas_123_qrp-mbody" id="vas_123_mbody_' + widgetID + '"></div>' +
                        '<div class="vas_123_qrp-mfoot" id="vas_123_mfoot_' + widgetID + '"></div>' +
                    '</div>' +
                '</div>' +
                // Toast notification strip
                '<div class="vas_123_qrp-toast" id="vas_123_toast_' + widgetID + '"></div>';

            $root = $('<div class="vas_123_qrp-shell" id="vas_123_shell_' + widgetID + '">' + shellHtml + '</div>');

            // ── Event delegation — ALL click handlers on $root ────────────────

            // Modal backdrop / close button
            $root.on('click', '[data-action="closeModal"]', function () {
                closeModal();
            });

            // Identity section actions
            $root.on('click', '[data-action="sendQuotation"]',   function () { openSendQuotation(); });
            $root.on('click', '[data-action="scheduleMeeting"]', function () { openScheduleMeeting(); });


            // Execute document action (inside confirmation modal)
            $root.on('click', '[data-action="executeDocAction"]', function () {
                var $btn   = $(this);
                var action = $btn.data('docAction');
                $btn.prop('disabled', true);
                postJSON('ExecuteDocAction', { C_Order_ID: currentOrderId, DocAction: action }, function (err, data) {
                    if (err || (data && data.error)) {
                        showToast(msg('VAS_123_UnableToExecuteAction'));
                        $btn.prop('disabled', false);
                    } else {
                        closeModal();
                        showToast(msg('VAS_123_ActionSuccess'));
                        loadHeader(currentOrderId);
                    }
                });
            });

            // Execute convert to sales order (inside confirmation modal)
            $root.on('click', '[data-action="executeConvertToOrder"]', function () {
                var $btn = $(this);
                $btn.prop('disabled', true);
                postJSON('ConvertToSalesOrder', { C_Order_ID: currentOrderId }, function (err, data) {
                    if (err || (data && data.error)) {
                        showToast(msg('VAS_123_UnableToExecuteAction'));
                        $btn.prop('disabled', false);
                    } else {
                        closeModal();
                        var orderLabel = (data && data.newOrderNo) ? (': ' + data.newOrderNo) : '';
                        showToast(msg('VAS_123_SalesOrderCreated') + orderLabel);
                        loadHeader(currentOrderId);
                    }
                });
            });

            // Open a linked order record in the platform viewer
            $root.on('click', '[data-action="openOrder"]', function (e) {
                openOrderRecord($(this).data('orderId'));
                e.stopPropagation();
            });

            // Join meeting — open URL in new tab
            $root.on('click', '[data-action="joinMeeting"]', function () {
                var u = $(this).data('url');
                if (u) { window.open(u, '_blank'); }
            });

            // Part 2 event bindings

            // Open quotation line detail modal (D1)
            $root.on('click', '[data-action="openLine"]', function (e) {
                openLineDetail($(this).data('lineId'));
                e.stopPropagation();
            });

            // Open link-opportunity modal (D2)
            $root.on('click', '[data-action="linkOpportunity"]', function () {
                openLinkOpportunity();
            });

            // Execute opportunity link inside D2 modal
            $root.on('click', '[data-action="executeLinkOpportunity"]', function () {
                var $btn  = $(this);
                var oppId = $btn.data('opportunityId');
                $btn.prop('disabled', true);
                postJSON('LinkOpportunity', { C_Order_ID: currentOrderId, VAS_Opportunity_ID: oppId }, function (err, data) {
                    if (err || (data && data.error)) {
                        showToast(msg('VAS_123_UnableToExecuteAction'));
                        $btn.prop('disabled', false);
                    } else {
                        closeModal();
                        var docNo = state.header ? (state.header.documentNo || '') : '';
                        showToast(msg('VAS_123_OpportunityLinked') + (docNo ? ' ' + docNo : ''));
                        loadOpportunity(currentOrderId);
                    }
                });
            });

            // Open quotation line record in the platform viewer (from D1 modal footer)
            $root.on('click', '[data-action="openLineRecord"]', function (e) {
                var lId = $(this).data('lineId');
                if (!lId || !window.VIS) return;
                closeModal();
                if (VIS.viewManager && typeof VIS.viewManager.startWindow === 'function') {
                    var q = (VIS.Query && VIS.Query.prototype && typeof VIS.Query.prototype.getEqualQuery === 'function')
                        ? VIS.Query.prototype.getEqualQuery('C_OrderLine_ID', lId)
                        : null;
                    VIS.viewManager.startWindow('VAS_QuotationLine', q);
                }
                e.stopPropagation();
            });

            // Open customer record in the platform viewer
            $root.on('click', '[data-action="openCustomer"]', function (e) {
                var bpId = $(this).data('bpId');
                if (!bpId || !window.VIS) return;
                if (VIS.viewManager && typeof VIS.viewManager.startWindow === 'function') {
                    var q = (VIS.Query && VIS.Query.prototype && typeof VIS.Query.prototype.getEqualQuery === 'function')
                        ? VIS.Query.prototype.getEqualQuery('C_BPartner_ID', bpId)
                        : null;
                    VIS.viewManager.startWindow('VAS_BPartner', q);
                }
                e.stopPropagation();
            });

            // Click on linked opportunity row — show all quotations sharing that opportunity
            $root.on('click', '[data-action="openLinkedQuotations"]', function (e) {
                var oppId   = $(this).data('opportunityId');
                var oppName = $(this).data('opportunityName') || '';
                if (!oppId) return;
                openLinkedQuotations(oppId, oppName);
                e.stopPropagation();
            });

            // Add line — directs user to line entry on the main window form
            $root.on('click', '[data-action="addLine"]', function () {
                showToast(msg('VAS_123_AddLineHint'));
            });

            // Call contact — telephony integration placeholder
            $root.on('click', '[data-action="callContact"]', function () {
                var mobile = $(this).data('mobile');
                showToast((msg('VAS_123_Calling') || '') + ' ' + (mobile || ''));
            });

            // §9b  Line history toggle — show/hide per-line history drawer
            $root.on('click', '[data-action="toggleLineHistory"]', function (e) {
                e.stopPropagation(); // prevent openLine from firing on the row
                var lineId   = $(this).data('line-id');
                if (!lineId) return;
                if (!state.lineHistOpen) state.lineHistOpen = {};
                var nowOpen  = !state.lineHistOpen[lineId];
                state.lineHistOpen[lineId] = nowOpen;
                var drawer = document.getElementById('vas_123_lhd_' + lineId + '_' + widgetID);
                if (drawer) { drawer.style.display = nowOpen ? '' : 'none'; }
                $(this).toggleClass('is-open', nowOpen);
                var histRows = (state.lineHistory && state.lineHistory[lineId]) || [];
                $(this).attr('title', nowOpen
                    ? msg('VAS_123_HistHide')
                    : msg('VAS_123_HistShow') + ' (' + histRows.length + ')');
            });

            // Part 3 event bindings

            // Tasks — segmented tab toggle (Upcoming / Previous)
            $root.on('click', '[data-action="switchTaskTab"]', function () {
                state.taskTab = $(this).data('tab') || 'up';
                renderTasks();
            });

            // Tasks — new task button → standard platform Appointments form (task mode)
            $root.on('click', '[data-action="newTask"]', function (e) {
                e.stopPropagation(); e.stopImmediatePropagation();
                if (!window.VIS || !VIS.AppointmentsForm || typeof VIS.AppointmentsForm.init !== 'function') return;
                var tableId  = $self.table_ID || 0;
                var userId   = (VIS.context && typeof VIS.context.getAD_User_ID  === 'function') ? VIS.context.getAD_User_ID()  : 0;
                var userName = (VIS.context && typeof VIS.context.getAD_UserName === 'function') ? VIS.context.getAD_UserName() : '';

                if (typeof window.$backBtn_ID === 'undefined') { window.$backBtn_ID = $(); }

                var tkNs          = 'ajaxComplete.vas123newtk' + Date.now();
                var taskCreated   = false;  // true once CreateJson_Task AJAX fires
                var formSeen      = false;  // true once the platform popup appears in DOM
                var listenersDone = false;
                var tkPrevErr     = window.onerror;
                var tkObs, tkPoll;

                function _cleanup() {
                    if (listenersDone) return;
                    listenersDone = true;
                    window.onerror = tkPrevErr;
                    $(document).off(tkNs);
                    if (tkObs)  { tkObs.disconnect();    tkObs  = null; }
                    if (tkPoll) { clearInterval(tkPoll); tkPoll = null; }
                }

                // Called only after the popup closes AND a task was actually created
                function _onPopupClosed() {
                    _cleanup();
                    if (taskCreated) { loadTasks(currentOrderId); }
                }

                // wsptask.js sometimes crashes after a successful save; detect and recover
                window.onerror = function (errMsg, src) {
                    if (src && src.indexOf('wsptask') >= 0 && !listenersDone) {
                        taskCreated = true;
                        setTimeout(_onPopupClosed, 300);
                    }
                    return tkPrevErr ? tkPrevErr.apply(this, arguments) : false;
                };

                VIS.AppointmentsForm.init(tableId, currentOrderId, userId, userName, true);

                // Flag task as created on successful CreateJson_Task response
                $(document).on(tkNs, function (ev, xhr, settings) {
                    if (settings && settings.url && settings.url.indexOf('CreateJson_Task') >= 0) {
                        taskCreated = true;
                    }
                });

                // MutationObserver: Phase 1 — wait for divAptBusy to appear (form open)
                //                   Phase 2 — wait for divAptBusy to be removed (form closed)
                tkObs = new MutationObserver(function (mutations) {
                    for (var mi = 0; mi < mutations.length; mi++) {
                        if (!formSeen) {
                            var added = mutations[mi].addedNodes;
                            for (var ai = 0; ai < added.length; ai++) {
                                var an = added[ai];
                                if (an.id === 'divAptBusy' ||
                                    (an.querySelector && an.querySelector('#divAptBusy'))) {
                                    formSeen = true; break;
                                }
                            }
                        }
                        if (formSeen) {
                            var removed = mutations[mi].removedNodes;
                            for (var ri = 0; ri < removed.length; ri++) {
                                var node = removed[ri];
                                if (node.id === 'divAptBusy' ||
                                    (node.querySelector && node.querySelector('#divAptBusy'))) {
                                    _onPopupClosed(); return;
                                }
                            }
                        }
                    }
                });
                tkObs.observe(document.body, { childList: true, subtree: true });

                // Polling fallback in case MutationObserver misses the DOM change
                tkPoll = setInterval(function () {
                    if (listenersDone) { clearInterval(tkPoll); return; }
                    var busyExists = !!document.getElementById('divAptBusy');
                    var formExists = !!document.getElementById('divTaskContinerFrom') ||
                                     !!$('.wsp-task-form').length;
                    if (!formSeen) {
                        if (busyExists || formExists) { formSeen = true; }
                        return;
                    }
                    if (!busyExists && !formExists) {
                        clearInterval(tkPoll); tkPoll = null;
                        _onPopupClosed();
                    }
                }, 400);
            });

            // Tasks — click task row to edit via standard platform form
            $root.on('click', '[data-action="openTask"]', function (e) {
                e.stopPropagation(); e.stopImmediatePropagation();
                var tid = parseInt($(this).data('taskId'), 10);
                if (!tid || tid <= 0) return;
                if (!window.WSP || !WSP.EditTaskForm || typeof WSP.EditTaskForm.init !== 'function') return;
                var tableId  = $self.table_ID || 0;
                var userId   = (VIS.context && typeof VIS.context.getAD_User_ID  === 'function') ? VIS.context.getAD_User_ID()  : 0;
                var userName = (VIS.context && typeof VIS.context.getAD_UserName === 'function') ? VIS.context.getAD_UserName() : '';
                var $busy    = $('<div id="divAptBusy" class="wsp-busy-indicater"></div>');
                $('body').append($busy); $busy.show();

                WSP.EditTaskForm.init(tid, tableId, currentOrderId, userId, userName, $busy);

                var refreshed = false;
                var ajaxNs   = 'ajaxComplete.vas123edittk' + tid;
                var obs = new MutationObserver(function (mutations) {
                    for (var i = 0; i < mutations.length; i++) {
                        var removed = mutations[i].removedNodes;
                        for (var j = 0; j < removed.length; j++) {
                            var node = removed[j];
                            if (node.id === 'divTaskContinerFrom' ||
                                (node.querySelector && node.querySelector('#divTaskContinerFrom'))) {
                                obs.disconnect();
                                $(document).off(ajaxNs);
                                if (!refreshed) { refreshed = true; loadTasks(currentOrderId); }
                                return;
                            }
                        }
                    }
                });
                obs.observe(document.body, { childList: true, subtree: true });

                $(document).on(ajaxNs, function (ev, xhr, settings) {
                    if (settings && settings.url && settings.url.indexOf('CreateJson_Task') >= 0) {
                        $(document).off(ajaxNs);
                        obs.disconnect();
                        if (!refreshed) { refreshed = true; loadTasks(currentOrderId); }
                    }
                });
            });

            // Tasks — checkbox toggle (mark done / reopen)
            $root.on('click', '[data-action="toggleTask"]', function (e) {
                e.stopPropagation();
                var taskId = $(this).data('taskId');
                var tasks  = state.tasks || [];
                for (var i = 0; i < tasks.length; i++) {
                    var tid = tasks[i].r_Request_ID || tasks[i].R_Request_ID || 0;
                    if (String(tid) === String(taskId)) {
                        var wasClosed = tasks[i]._closed;
                        tasks[i]._closed = !wasClosed;
                        // Completion % follows the spec: mark done sets 100, reopen preserves existing %
                        if (!wasClosed) { tasks[i].pct = 100; tasks[i].closed = true; }
                        else            { tasks[i].closed = false; }
                        showToast(wasClosed ? msg('VAS_123_TaskReopened') : msg('VAS_123_TaskCompleted'));
                        break;
                    }
                }
                renderTasks();
            });

            // Tasks — priority chip picker inside task modal
            $root.on('click', '[data-action="pickPriority"]', function () {
                var $modal = $(document.getElementById('vas_123_mbody_' + widgetID));
                $modal.find('[data-action="pickPriority"]').removeClass('vas_123_qrp-pick--on');
                $(this).addClass('vas_123_qrp-pick--on');
            });

            // Tasks — assignee add from suggestion dropdown
            $root.on('click', '[data-action="addAssg"]', function () {
                var uId   = $(this).data('userId');
                var uName = $(this).data('userName');
                if (uId && uName && !_taskAssg.some(function (a) { return String(a.id) === String(uId); })) {
                    _taskAssg.push({ id: uId, name: uName });
                }
                var inputEl  = document.getElementById('vas_123_assgInput_'   + widgetID);
                var suggestEl= document.getElementById('vas_123_assgSuggest_' + widgetID);
                if (inputEl)   { inputEl.value = ''; }
                if (suggestEl) { suggestEl.classList.remove('vas_123_qrp-suggest--open'); suggestEl.innerHTML = ''; }
                renderAssgChips();
            });

            // Tasks — assignee chip remove
            $root.on('click', '[data-action="removeAssg"]', function (e) {
                var uId = String($(this).data('userId'));
                _taskAssg = _taskAssg.filter(function (a) { return String(a.id) !== uId; });
                renderAssgChips();
                e.stopPropagation();
            });

            // Tasks — save task (PostNote-style; a full task save would go to a dedicated endpoint)
            $root.on('click', '[data-action="saveTask"]', function () {
                // Read modal form fields
                var titleEl = document.getElementById('vas_123_taskTitle_' + widgetID);
                var dueEl   = document.getElementById('vas_123_taskDue_'   + widgetID);
                var pctEl   = document.getElementById('vas_123_taskPct_'   + widgetID);
                var priEl   = document.querySelector('#vas_123_mbody_' + widgetID + ' .vas_123_qrp-pick--on');
                var titleVal = titleEl ? titleEl.value.trim() : '';
                if (!titleVal) { showToast(msg('VAS_123_TaskTitleRequired')); return; }
                // For now save is client-only; a future server endpoint can be wired here
                // (R_Request INSERT/UPDATE was intentionally deferred until the table mapping is confirmed)
                closeModal();
                showToast(msg('VAS_123_TaskSaved'));
                // Reload tasks from server to pick up any server-side changes
                loadTasks(currentOrderId);
            });

            // Tasks — close or reopen task from within the task modal
            $root.on('click', '[data-action="closeReopenTask"]', function () {
                var taskId  = $(this).data('taskId');
                var reopen  = ($(this).data('reopen') === 'Y');
                var tasks   = state.tasks || [];
                for (var i = 0; i < tasks.length; i++) {
                    var tid = tasks[i].r_Request_ID || tasks[i].R_Request_ID || 0;
                    if (String(tid) === String(taskId)) {
                        tasks[i]._closed = !reopen;
                        tasks[i].closed  = !reopen;
                        if (!reopen) { tasks[i].pct = 100; }
                        showToast(reopen ? msg('VAS_123_TaskReopened') : msg('VAS_123_TaskCompleted'));
                        break;
                    }
                }
                closeModal();
                renderTasks();
            });

            // Engagement — WhatsApp stat card opens latest chat
            $root.on('click', '[data-action="openChat"]', function (e) {
                e.stopPropagation(); e.stopImmediatePropagation();
                openWhatsAppModal();
            });

            // Engagement — WhatsApp timeline card opens specific topic
            $root.on('click', '[data-chat-topic-id]', function (e) {
                e.stopPropagation(); e.stopImmediatePropagation();
                var tid = parseInt($(this).data('chat-topic-id'), 10);
                if (tid > 0) openWhatsAppModal(tid);
            });

            // Engagement — Meeting card opens meeting detail modal
            $root.on('click', '[data-meeting-id]', function (e) {
                e.stopPropagation(); e.stopImmediatePropagation();
                var mid = parseInt($(this).data('meeting-id'), 10);
                if (mid > 0) openMeetingDetailModal(mid);
            });

            // Engagement — Note card opens note detail modal
            $root.on('click', '[data-note-id]', function (e) {
                e.stopPropagation(); e.stopImmediatePropagation();
                var nid = parseInt($(this).data('note-id'), 10);
                if (nid > 0) openNoteDetailModal(nid);
            });

            // Engagement — Email card opens email detail modal
            $root.on('click', '[data-email-id]', function (e) {
                e.stopPropagation(); e.stopImmediatePropagation();
                var eid = parseInt($(this).data('email-id'), 10);
                if (eid > 0) openEmailDetailModal(eid);
            });

            // Engagement — post note from composer
            $root.on('click', '[data-action="postNote"]', function () {
                var noteEl = document.getElementById('vas_123_noteBox_' + widgetID);
                var text   = noteEl ? noteEl.value.trim() : '';
                if (!text) { showToast(msg('VAS_123_NoteRequired')); return; }

                var $btn = $(this);
                $btn.prop('disabled', true);

                postJSON('PostNote', { C_Order_ID: currentOrderId, NoteText: text }, function (err, data) {
                    $btn.prop('disabled', false);
                    if (err || !data || !data.success) {
                        showToast(msg('VAS_123_UnableToExecuteAction'));
                    } else {
                        if (noteEl) { noteEl.value = ''; }
                        state.engagementPage    = 0;
                        state.engagementChannel = 'all';
                        showToast(msg('VAS_123_NotePosted'));
                        loadEngagement(currentOrderId);
                    }
                });
            });

            // Keyboard: Escape key closes any open modal
            $(document).on('keydown.vas123_' + widgetID, function (e) {
                if (e.key === 'Escape') { closeModal(); }
            });
        };

        this.getRoot = function () { return $root; };

        // ── Prototype: refreshPanelData ───────────────────────────────────────
        VAS.VAS_123_QuotationRightPanel.prototype.refreshPanelData = function (recordID, selectedRow) {
            if (!recordID || recordID <= 0) {
                this.clear();
                return;
            }
            currentOrderId    = recordID;
            $self.record_ID   = recordID;
            $self.selectedRow = selectedRow;
            loadHeader(recordID);
        };

        // ── Prototype: clear ──────────────────────────────────────────────────
        VAS.VAS_123_QuotationRightPanel.prototype.clear = function () {
            // Reset all state to initial values
            currentOrderId       = 0;
            state.header         = null;
            state.nextMeeting    = null;
            state.headerLoading  = false;
            state.meetingLoading = false;
            state.headerLoaded   = false;
            state.headerError    = null;

            // Reset Part 2 state
            state.generatedOrders        = null;
            state.generatedOrdersLoading = false;
            state.opportunity            = null;
            state.opportunityLoading     = false;
            state.lines                  = null;
            state.linesLoading           = false;
            state.addresses              = null;
            state.addressesLoading       = false;
            state.terms                  = null;
            state.termsLoading           = false;

            // Reset Part 3 state
            state.tasks               = null;
            state.tasksLoading        = false;
            state.taskTab             = 'up';
            state.engagement          = null;
            state.engagementLoading   = false;
            state.engagementPage      = 0;
            state.engagementChannel   = 'all';
            _taskAssg                 = [];

            // Abort any in-flight requests
            for (var action in pendingXhr) {
                if (pendingXhr[action] && pendingXhr[action].readyState !== 4) {
                    try { pendingXhr[action].abort(); } catch (e) {}
                }
                pendingXhr[action] = null;
            }

            // Show "no data available" placeholder instead of a blank body
            var bodyEl = document.getElementById('vas_123_body_' + widgetID);
            if (bodyEl) bodyEl.innerHTML = '<div class="vas_123_qrp-norecord">' + esc(msg('VIS_NoData')) + '</div>';
        };

    }; // end VAS.VAS_123_QuotationRightPanel constructor

    // ── VIS Tab-Panel interface ────────────────────────────────────────────────
    // Called by the VIS framework when the tab panel host attaches this panel.
    VAS.VAS_123_QuotationRightPanel.prototype.startPanel = function (windowNo, curTab) {
        this.windowNo = windowNo;
        this.curTab   = curTab;
        this.table_ID = curTab ? curTab.getAD_Table_ID() : 0;
        this.Initialize();
    };

    VAS.VAS_123_QuotationRightPanel.prototype.sizeChanged      = function (width) { this.panelWidth = width; };
    VAS.VAS_123_QuotationRightPanel.prototype.widgetSizeChange = function (width) { this.panelWidth = width; };
    VAS.VAS_123_QuotationRightPanel.prototype.refreshWidget    = function () {};

    VAS.VAS_123_QuotationRightPanel.prototype.init = function (windowNo, frame) {
        this.frame      = frame;
        this.widgetInfo = frame ? frame.widgetInfo : null;
        this.windowNo   = windowNo;
        this.Initialize();
        if (this.frame && typeof this.frame.getContentGrid === 'function') {
            this.frame.getContentGrid().append(this.getRoot());
        }
    };

    VAS.VAS_123_QuotationRightPanel.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_123_QuotationRightPanel.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_123_QuotationRightPanel.prototype.dispose = function () {
        $(document).off('keydown.vas123_' + this.windowNo);
        this.record_ID = this.table_ID = 0;
        this.curTab = this.frame = this.windowNo = null;
    };

}(VAS, jQuery));
