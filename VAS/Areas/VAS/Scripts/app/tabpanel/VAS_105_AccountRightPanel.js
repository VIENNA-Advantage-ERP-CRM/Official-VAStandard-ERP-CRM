/********************************************************
 * Module Name    : CRM Extension VAS
 * Purpose        : Account Right Detail Panel — client logic
 * Employee Code  : VAI154
 * Date           : 09-Jun-2026
 *
 * Chronological development:
 *   VAI163   2026-08-06  The Engagement timeline paginates at 15 touches a page
 *                        (ENGAGEMENT_PER_PAGE), reusing the of-lu-pager shell the
 *                        Orders / Invoices panels use. Unlike those it pages
 *                        client-side — the whole timeline already arrives in one
 *                        payload — so prev/next re-enter renderEngagement rather
 *                        than re-fetching. The stat strip above the rail still
 *                        counts every touch, and an account with 15 or fewer shows
 *                        no controls at all.
 ******************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_105_AccountRightPanel = function () {
        this.frame;
        this.windowNo;
        this.listener;

        var $self     = this;
        var $root     = null;
        var widgetID  = null;
        var currentBpId = 0;
        var pendingXhr  = {};

        // Add-entry-point state
        // BPartner records have no DocStatus — always editable; flag reserved for future state-based hiding
        var _bpIsEditable  = true;
        var _bpSectionCounts = {};  // sectionId → item count; undefined = not yet loaded

        // Task filter state
        var taskFilter  = 'upcoming';  // 'upcoming' | 'previous'

        // Per-section state: { loading, error, data, loaded }
        var sectionState = {};
        var SECTIONS = ['overview','contacts','locations','opps','contracts',
                        'tickets','orders','invoices','projects','tasks','timeline'];
        for (var _si = 0; _si < SECTIONS.length; _si++) {
            sectionState[SECTIONS[_si]] = { loading: false, error: null, data: null, loaded: false };
        }

        // Orders / invoices pagination (inline panel: 5 rows each)
        var PAGE_SIZE_INLINE  = 5;
        var _ordersOffset     = 0;
        var _invoicesOffset   = 0;

        // Engagement timeline client-side pagination (15 touches per page)
        var ENGAGEMENT_PER_PAGE = 15;
        var _engPage            = 0;


        // Opps / contracts client-side pagination (5 rows per page)
        var OPP_PAGE_SIZE  = 5;
        var CT_PAGE_SIZE   = 5;
        var _oppsPage      = 0;
        var _contractsPage = 0;


        // ── Helpers ──────────────────────────────────────────────────────────
        function esc(v) {
            return String(v == null ? '' : v)
                .replace(/&/g,'&amp;').replace(/</g,'&lt;')
                .replace(/>/g,'&gt;').replace(/"/g,'&quot;')
                .replace(/'/g,'&#039;');
        }
        function toNum(v) { var n = Number(v); return isFinite(n) ? n : 0; }
        function msg(k)   { return (VIS && VIS.Msg && VIS.Msg.getMsg) ? (VIS.Msg.getMsg(k) || k) : k; }

        function getSym()  { var d = sectionState.overview.data; return (d && d.currencySymbol) ? String(d.currencySymbol) : ''; }
        function getPrec() { var d = sectionState.overview.data; return (d && d.precision != null) ? Math.max(0, parseInt(d.precision, 10) || 0) : 2; }

        function fmtM(v, sym, prec) {
            var s = (sym != null ? sym : getSym());
            var p = (prec != null ? Math.max(0, parseInt(prec, 10) || 0) : getPrec());
            var abs = Math.abs(v);
            if (abs >= 1e6) return s + (v / 1e6).toFixed(Math.min(p, 1)).replace(/\.0$/, '') + 'M';
            if (abs >= 1e3) return s + (abs >= 1e5
                ? String(Math.round(v / 1e3))
                : (v / 1e3).toFixed(Math.min(p, 1)).replace(/\.0$/, '')) + 'K';
            return s + toNum(v).toFixed(p).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
        }

        // Full exact amount — no K/M/B suffix; used in single-record detail views.
        function fmtFull(v, sym, prec) {
            var s = (sym != null ? sym : getSym());
            var p = (prec != null ? Math.max(0, parseInt(prec, 10) || 0) : getPrec());
            return s + toNum(v).toFixed(p).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
        }

        function fmtDate(iso) {
            if (!iso) return '';
            // iso is YYYY-MM-DD
            var parts = String(iso).split('-');
            if (parts.length === 3) return parts[2] + ' ' + ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'][parseInt(parts[1],10)-1] + ' ' + parts[0];
            return iso;
        }

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

        function initials(name) {
            return String(name || '').split(/\s+/).map(function (w) { return w.charAt(0); }).slice(0, 2).join('').toUpperCase() || '?';
        }

        // Returns 3-star rating HTML — filled stars amber, unfilled stars light gray
        function ratingStars(tier) {
            var filled = tier === 'Platinum' ? 3 : tier === 'Gold' ? 2 : 1;
            var h = '';
            for (var i = 1; i <= 3; i++) {
                h += '<span style="color:' + (i <= filled ? '#F5A623' : '#D0D8E0') + ';font-size:1.1em;line-height:1;">&#9733;</span>';
            }
            return h;
        }

        // ── Section IDs ───────────────────────────────────────────────────────
        function secId(name) { return 'vas_105_sec_' + widgetID + '_' + name; }

        // ── AJAX fetch ────────────────────────────────────────────────────────
        function fetchSection(secName, action, extra, callback) {
            if (pendingXhr[secName] && pendingXhr[secName].readyState !== 4) {
                try { pendingXhr[secName].abort(); } catch (e) { /* ignore */ }
            }
            sectionState[secName].loading = true;
            sectionState[secName].error   = null;
            renderSec(secName);

            var postData = $.extend({ bPartnerId: currentBpId }, extra || {});

            pendingXhr[secName] = $.ajax({
                url:   VIS.Application.contextUrl + 'VAS/VAS_105_AccountRightPanel/' + action,
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

        // ── Modal ─────────────────────────────────────────────────────────────
        function showModal(title, meta, bodyHtml, footHtml, wide) {
            var overlay = document.getElementById('vas_105_overlay_' + widgetID);
            if (!overlay) return;
            document.getElementById('vas_105_mtitle_' + widgetID).textContent = title || '';
            document.getElementById('vas_105_mmeta_'  + widgetID).textContent = meta  || '';
            document.getElementById('vas_105_mbody_'  + widgetID).innerHTML   = bodyHtml || '';
            document.getElementById('vas_105_mfoot_'  + widgetID).innerHTML   = footHtml || '';
            var modal = document.getElementById('vas_105_modal_' + widgetID);
            if (wide) modal.classList.add('vas_105_acct-modal--wide');
            else      modal.classList.remove('vas_105_acct-modal--wide');
            overlay.classList.add('vas_105_acct-overlay--open');
        }

        function closeModal() {
            var overlay = document.getElementById('vas_105_overlay_' + widgetID);
            if (overlay) overlay.classList.remove('vas_105_acct-overlay--open');
        }

        // ── fetchModal — one-shot AJAX to VAS_105_AccountRightPanel (no section state) ──
        function fetchModal(action, extra, callback) {
            $.ajax({
                url:      VIS.Application.contextUrl + 'VAS/VAS_105_AccountRightPanel/' + action,
                type:     'POST',
                dataType: 'json',
                data:     extra || {},
                success:  function (raw) {
                    var data;
                    try { data = (typeof raw === 'string') ? jQuery.parseJSON(raw) : raw; } catch (e) { data = raw; }
                    callback(null, data);
                },
                error: function (xhr, status, err) {
                    callback(err || status, null);
                }
            });
        }

        // ── WhatsApp conversation modal ────────────────────────────────────────
        function openWhatsAppModal(topicId) {
            var SVG_WHATSAPP = '<svg width="1.1em" height="1.1em" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M21 11.5a8.38 8.38 0 0 1-.9 3.8 8.5 8.5 0 0 1-7.6 4.7 8.38 8.38 0 0 1-3.8-.9L3 21l1.9-5.7a8.38 8.38 0 0 1-.9-3.8 8.5 8.5 0 0 1 4.7-7.6 8.38 8.38 0 0 1 3.8-.9h.5a8.48 8.48 0 0 1 8 8v.5z"/></svg>';
            var SVG_SEND     = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="22" y1="2" x2="11" y2="13"/><polygon points="22 2 15 22 11 13 2 9 22 2"/></svg>';

            showModal(msg('VAS_105_WhatsApp'), '', '<div class="vas_105_acct-empty">' + esc(msg('VAS_105_Loading')) + '</div>', '', true);

            fetchModal('GetWhatsAppChat', { bPartnerId: currentBpId, topicId: topicId || 0 }, function (err, data) {
                if (err || !data || !data.topic) {
                    document.getElementById('vas_105_mbody_' + widgetID).innerHTML =
                        '<div class="vas_105_acct-empty">' + esc(msg('VIS_NoData')) + '</div>';
                    document.getElementById('vas_105_mfoot_' + widgetID).innerHTML =
                        '<button class="vas_105_acct-btn vas_105_acct-btn--secondary" id="' + widgetID + '_wachatClose">' + esc(msg('Close')) + '</button>';
                    $root.find('#' + widgetID + '_wachatClose').on('click', function (e) {
                        e.stopPropagation(); e.stopImmediatePropagation(); closeModal();
                    });
                    return;
                }

                var topic    = data.topic    || {};
                var messages = data.messages || [];

                var contactHtml = [
                    '<div class="vas_105_wachat-contact">',
                    '  <span class="vas_105_wachat-avatar">', SVG_WHATSAPP, '</span>',
                    '  <div>',
                    '    <div class="vas_105_wachat-contact-name">', esc(topic.contactName || ''), '</div>',
                    '    <div class="vas_105_wachat-contact-meta">', esc(msg('VAS_105_WhatsApp')),
                             topic.chatDate ? ' &middot; ' + esc(topic.chatDate) : '',
                    '    </div>',
                    '  </div>',
                    '</div>'
                ].join('');

                var bubblesHtml = '<div class="vas_105_wachat-bubbles" id="' + widgetID + '_wachatBubbles">';
                if (messages.length === 0) {
                    bubblesHtml += '<div class="vas_105_acct-empty">' + esc(msg('VAS_105_NoEngagement')) + '</div>';
                } else {
                    for (var i = 0; i < messages.length; i++) {
                        var m        = messages[i];
                        var isSender = (m.isSender === 'Y');
                        var bubCls   = 'vas_105_wachat-bubble ' + (isSender ? 'vas_105_wachat-bubble--out' : 'vas_105_wachat-bubble--in');
                        bubblesHtml += [
                            '<div class="', bubCls, '">',
                            '  <div class="vas_105_wachat-text">', esc(m.textMsg || ''), '</div>',
                            '  <div class="vas_105_wachat-ts">', esc(m.msgDate || ''), isSender ? ' ✓✓' : '', '</div>',
                            '</div>'
                        ].join('');
                    }
                }
                bubblesHtml += '</div>';

                var sendHtml = [
                    '<div class="vas_105_wachat-sendbox">',
                    '  <input type="text" class="vas_105_wachat-input" id="', widgetID, '_wachatInput"',
                    '    placeholder="', esc(msg('VAS_105_TypeMessage')), '" />',
                    '  <button class="vas_105_wachat-sendbtn" id="', widgetID, '_wachatSend">', SVG_SEND, '</button>',
                    '</div>'
                ].join('');

                var footHtml = '<button class="vas_105_acct-btn vas_105_acct-btn--secondary" id="' + widgetID + '_wachatClose">' + esc(msg('Close')) + '</button>';

                document.getElementById('vas_105_mbody_' + widgetID).innerHTML = contactHtml + bubblesHtml + sendHtml;
                document.getElementById('vas_105_mfoot_' + widgetID).innerHTML = footHtml;

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
                    fetchModal('GetWhatsAppTopicMeta', { topicId: resolvedTopicId }, function (err1, meta) {
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

        // ── Pair-row helpers (company info grid) ──────────────────────────────
        function pairRow(k, v, last) {
            if (!v) return '';
            return '<div class="vas_105_acct-pair"><span class="vas_105_acct-pair__k">' + esc(k) + '</span>' +
                   '<span class="vas_105_acct-pair__v">' + esc(v) + '</span></div>';
        }
        function pairRowLink(k, v, href) {
            if (!v) return '';
            var valHtml = href
                ? '<a href="' + esc(href) + '" target="_blank" rel="noopener" style="color:var(--acct-link);">' + esc(v) + '</a>'
                : esc(v);
            return '<div class="vas_105_acct-pair"><span class="vas_105_acct-pair__k">' + esc(k) + '</span>' +
                   '<span class="vas_105_acct-pair__v">' + valHtml + '</span></div>';
        }

        // ── Loading skeleton ──────────────────────────────────────────────────
        function skelLines(n) {
            var h = '';
            for (var i = 0; i < n; i++) {
                h += '<div class="vas_105_acct-skel vas_105_acct-skel--line" style="margin-bottom:0.4em;width:' + (50 + (i * 13 % 40)) + '%;"></div>';
            }
            return h;
        }

        function emptyState(msgKey) {
            return '<div class="vas_105_acct-empty">' + esc(msg(msgKey)) + '</div>';
        }

        // ── renderSec dispatcher ──────────────────────────────────────────────
        function renderSec(secName) {
            var el = $root ? $root.find('#' + secId(secName))[0] : document.getElementById(secId(secName));
            if (!el) return;
            var s = sectionState[secName];
            if (s.loading) { el.innerHTML = skelLines(3); return; }
            if (s.error)   { el.innerHTML = '<div class="vas_105_acct-errtxt">' + esc(msg('VAS_105_LoadError')) + '</div>'; return; }
            switch (secName) {
                case 'overview':   renderOverview(el, s.data);   break;
                case 'contacts':   renderContacts(el, s.data);   break;
                case 'locations':  renderLocations(el, s.data);  break;
                case 'opps':       renderOpps(el, s.data);       break;
                case 'contracts':  renderContracts(el, s.data);  break;
                case 'tickets':    renderTickets(el, s.data);    break;
                case 'tasks':      el.innerHTML = renderTasks(s.data); break;
                case 'orders':     renderOrders(el, s.data);     break;
                case 'invoices':   renderInvoices(el, s.data);   break;
                case 'projects':   renderProjects(el, s.data);   break;
                case 'timeline':   renderEngagement(el, s.data); break;
            }
            // After successful render, show or hide the section wrapper based on data.
            applySecVisibility(secName, getSectionItemCount(secName, s.data));
        }

        // ── renderOverview ────────────────────────────────────────────────────
        function renderOverview(el, data) {
            if (!data || data.error === 'not_found') {
                el.innerHTML = emptyState('VAS_105_SelectAccount');
                return;
            }

            var sym  = data.currencySymbol || '';
            var prec = (data.precision != null) ? Math.max(0, parseInt(data.precision, 10) || 0) : 2;

            var ini  = initials(data.name);
            var arr  = toNum(data.annualRevenue);
            var arrStr = fmtM(arr, sym, prec);

            var rnDays = data.renewalInDays;
            var renewalStr   = (rnDays != null && rnDays !== '') ? (String(rnDays) + ' ' + msg('VIS_Days')) : '';
            var renewalStyle = (rnDays != null && toNum(rnDays) < 90 && toNum(rnDays) >= 0) ? 'color:#9A6500;' : '';

            var tierKey = { 'Platinum': 'vas_105_acct-tier--platinum', 'Gold': 'vas_105_acct-tier--gold' };
            var tierCls  = tierKey[data.tier] || 'vas_105_acct-tier--silver';
            var tierHtml = ratingStars(data.tier);


            var subParts = [data.industry, data.segment, data.owner ? msg('VAS_105_Owner') + ' ' + data.owner : null].filter(Boolean);

            var html =
                '<div class="vas_105_acct-identity">' +
                  '<div class="vas_105_acct-avatar">' + esc(ini) + '</div>' +
                  '<div style="min-width:0;">' +
                    '<div style="display:flex;align-items:center;gap:0.625em;flex-wrap:wrap;">' +
                      '<span style="font-size:1.5em;font-weight:700;color:var(--acct-text);">' + esc(data.name || '') + '</span>' +
                      '<span class="vas_105_acct-tag vas_105_acct-tag--customer">' + esc(msg('Customer')) + '</span>' +
                      '<span class="vas_105_acct-tag ' + tierCls + '" style="letter-spacing:0.1em;">' + tierHtml + '</span>' +
                    '</div>' +
                    '<div class="vas_105_acct-id-sub">' + esc(subParts.join(' · ')) + '</div>' +
                  '</div>' +
                  '<div class="vas_105_acct-id-actions">' +
                    '<button class="vas_105_acct-btn vas_105_acct-btn--secondary" id="vas_105_btn_sched_' + widgetID + '">' +
                      '<i class="ti ti-calendar-plus" style="font-size:1em;"></i>' + esc(msg('VAS_105_ScheduleMeeting')) +
                    '</button>' +
                  '</div>' +
                '</div>' +

                '<div class="vas_105_acct-kpi" style="margin-top:1.5em;">' +
                  '<div><div class="vas_105_acct-kpi__lbl">' + esc(msg('VAS_105_AnnualRevenue')) + '</div>' +
                       '<div class="vas_105_acct-kpi__val">' + esc(arrStr) + '</div></div>' +
                  '<div><div class="vas_105_acct-kpi__lbl">' + esc(msg('VAS_105_HealthScore')) + '</div>' +
                       '<div class="vas_105_acct-kpi__val" style="line-height:1;">' + ratingStars(data.tier) + '</div></div>' +
                  '<div><div class="vas_105_acct-kpi__lbl">' + esc(msg('VAS_105_OpenOpps')) + '</div>' +
                       '<div class="vas_105_acct-kpi__val">' + esc(String(data.openOpportunities || 0)) + '</div></div>' +
                  (renewalStr
                      ? '<div><div class="vas_105_acct-kpi__lbl">' + esc(msg('VAS_105_RenewalIn')) + '</div>' +
                             '<div class="vas_105_acct-kpi__val" style="' + renewalStyle + '">' + esc(renewalStr) + '</div></div>'
                      : '') +
                '</div>' +

                '<div style="margin-top:1.75em;">' +
                  '<div class="vas_105_acct-sectitle">' + esc(msg('CompanyInfo')) + '</div>' +
                  '<div class="vas_105_acct-pairgrid" style="margin-top:0.625em;">' +
                    pairRow(msg('VAS_105_Industry'), data.industry, false) +
                    pairRow(msg('VAS_105_Employees'), data.employees ? Number(data.employees).toLocaleString() : '', false) +
                    pairRow(msg('VAS_105_AccountID'), data.accountCode, false) +
                    pairRow(msg('VAS_105_Region'), data.region, false) +
                    pairRowLink(msg('VAS_105_Website'), data.website, data.website) +
                    pairRow(msg('VAS_105_Owner'), data.owner, true) +
                  '</div>' +
                '</div>';

            el.innerHTML = html;

            // Wire action buttons
            var btnSched = document.getElementById('vas_105_btn_sched_' + widgetID);
            if (btnSched) btnSched.onclick = function (e) { if (e) { e.stopPropagation(); e.stopImmediatePropagation(); } openSchedule(); };
        }

        // ── renderContacts ────────────────────────────────────────────────────
        function renderContacts(el, data) {
            var items = (data && data.items) ? data.items : [];

            // Update count badge
            var cntEl = document.getElementById(secId('contacts') + '_cnt');
            if (cntEl) cntEl.textContent = items.length > 0 ? String(items.length) : '';

            if (!items.length) { el.innerHTML = emptyState('VAS_105_NoContacts'); return; }

            // Inline SVG icons (stroke="currentColor", colour set via parent style — mirrors Latest Updates pattern)
            var SVG_PHONE =
                '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
                '<path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07A19.5 19.5 0 0 1 4.99 12 19.79 19.79 0 0 1 1.95 3.18 2 2 0 0 1 3.93 1h3a2 2 0 0 1 2 1.72 12.84 12.84 0 0 0 .7 2.81 2 2 0 0 1-.45 2.11L8.09 8.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45 12.84 12.84 0 0 0 2.81.7A2 2 0 0 1 22 16.92z"/>' +
                '</svg>';
            var SVG_MAIL =
                '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
                '<rect width="20" height="16" x="2" y="4" rx="2"/>' +
                '<path d="m22 7-8.97 5.7a1.94 1.94 0 0 1-2.06 0L2 7"/>' +
                '</svg>';

            var avColors = [
                { bg:'#EAF8FF', fg:'#0083DA' }, { bg:'#F3F0FF', fg:'#5F4AA6' },
                { bg:'#EAF6EF', fg:'#0B6B45' }, { bg:'#FBF3E2', fg:'#9A6500' },
                { bg:'#FCEFEF', fg:'#A33F3F' }
            ];

            var html = '<div class="vas_105_acct-contactgrid">';
            for (var i = 0; i < items.length; i++) {
                var c  = items[i];
                var av = avColors[i % 5];
                html +=
                    '<div class="vas_105_acct-contact">' +
                      '<div class="vas_105_acct-contact__av" style="background:' + av.bg + ';color:' + av.fg + ';">' + esc(initials(c.name)) + '</div>' +
                      '<div style="min-width:0;flex:1;">' +
                        '<div style="display:flex;align-items:center;gap:0.375em;flex-wrap:wrap;">' +
                          '<span class="vas_105_acct-contact__name">' + esc(c.name || '') + '</span>' +
                        '</div>' +
                        (c.title ? '<div class="vas_105_acct-contact__title">' + esc(c.title) + '</div>' : '') +
                        (c.phone
                          ? '<div class="vas_105_acct-contact__phone" style="color:var(--acct-text);">' +
                              '<span style="color:var(--acct-muted);">' + SVG_PHONE + '</span>' +
                              esc(c.phone) +
                            '</div>'
                          : '') +
                      '</div>' +
                      (c.email
                        ? '<button type="button" class="vas_105_acct-contact__mailbtn" data-action="contactEmail" data-email="' + esc(c.email) + '" aria-label="' + esc(msg('VAS_Email') + ' ' + (c.name || '')) + '" title="' + esc(c.email) + '">' +
                            SVG_MAIL +
                          '</button>'
                        : '') +
                    '</div>';
            }
            html += '</div>';
            el.innerHTML = html;
        }

        // ── renderLocations ───────────────────────────────────────────────────
        function renderLocations(el, data) {
            var items = (data && data.items) ? data.items : [];

            var cntEl = document.getElementById(secId('locations') + '_cnt');
            if (cntEl) cntEl.textContent = items.length > 0 ? (String(items.length) + ' ' + msg('VAS_105_Sites')) : '';

            if (!items.length) { el.innerHTML = emptyState('VAS_105_NoLocations'); return; }

            var html = '<div class="vas_105_acct-locgrid">';
            for (var i = 0; i < items.length; i++) {
                var l = items[i];
                var addrParts = [l.address, l.city, l.postal, l.regionName, l.country].filter(Boolean);
                html +=
                    '<div class="vas_105_acct-loccard">' +
                      '<div class="vas_105_acct-loccard__hd">' +
                        '<span class="vas_105_acct-loccard__name">' + esc(l.siteName || '') + '</span>' +
                        '<span class="vas_105_acct-loccard__type">' + esc(l.locType || '') + '</span>' +
                      '</div>' +
                      '<div class="vas_105_acct-loccard__adr">' +
                        '<i class="ti ti-map-pin" style="font-size:0.8125em;color:var(--acct-muted);vertical-align:-0.125em;margin-right:0.25em;"></i>' +
                        esc(addrParts.join(', ')) +
                      '</div>' +
                      (l.primaryContact
                        ? '<div class="vas_105_acct-loccard__meta">' +
                          '<span><i class="ti ti-user" style="font-size:0.8125em;vertical-align:-0.125em;margin-right:0.1875em;"></i>' + esc(l.primaryContact) + '</span>' +
                          '</div>'
                        : '') +
                    '</div>';
            }
            html += '</div>';
            el.innerHTML = html;
        }

        // ── Section pagination bar (Opps / Contracts) ────────────────────────
        // Returns HTML for the "Showing X – Y of Z | < N of M >" bar.
        // Returns '' when all items fit on one page.
        function buildSectionPager(secKey, totalItems, currentPage, pageSize) {
            if (totalItems <= pageSize) return '';
            var totalPages   = Math.ceil(totalItems / pageSize);
            var start        = currentPage * pageSize + 1;
            var end          = Math.min(start + pageSize - 1, totalItems);
            var prevDisabled = currentPage <= 0;
            var nextDisabled = currentPage >= totalPages - 1;
            var chevL = '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>';
            var chevR = '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>';
            return '<div class="vas_105_acct-secpager">' +
                     '<span class="vas_105_acct-secpager__info">' +
                       esc(msg('VAS_040_Showing')) + ' <b>' + start + ' &ndash; ' + end + '</b> ' + esc(msg('of')) + ' <b>' + totalItems + '</b>' +
                     '</span>' +
                     '<nav class="vas_105_acct-secpager__nav" role="navigation">' +
                       '<button type="button" class="vas_105_acct-secpager__btn" id="vas_105_' + secKey + 'prev_' + widgetID + '"' + (prevDisabled ? ' disabled' : '') + '>' + chevL + '</button>' +
                       '<span class="vas_105_acct-secpager__page"><b>' + (currentPage + 1) + '</b>&nbsp;' + esc(msg('of')) + '&nbsp;' + totalPages + '</span>' +
                       '<button type="button" class="vas_105_acct-secpager__btn" id="vas_105_' + secKey + 'next_' + widgetID + '"' + (nextDisabled ? ' disabled' : '') + '>' + chevR + '</button>' +
                     '</nav>' +
                   '</div>';
        }

        // ── renderOpps ────────────────────────────────────────────────────────
        // Accepts both stageCode and stageName; stageName (from AD_Ref_List) is preferred for matching
        // because actual DB code values vary per environment.
        function stageClass(code, name) {
            var n = (name || '').toLowerCase();
            if (/prospect/i.test(n))         return 'vas_105_acct-stage--prospecting';
            if (/qualif/i.test(n))           return 'vas_105_acct-stage--qualification';
            if (/discover|design/i.test(n))  return 'vas_105_acct-stage--discovery';
            if (/proposal|quote/i.test(n))   return 'vas_105_acct-stage--proposal';
            if (/negotiat/i.test(n))         return 'vas_105_acct-stage--negotiation';
            if (/follow/i.test(n))           return 'vas_105_acct-stage--followup';
            if (/won/i.test(n))              return 'vas_105_acct-stage--won';
            if (/closed|complete/i.test(n))  return 'vas_105_acct-stage--closed';
            if (/lost|archiv/i.test(n))      return 'vas_105_acct-stage--lost';
            if (/hold|pause/i.test(n))       return 'vas_105_acct-stage--hold';
            // Fallback: known legacy codes
            var codeMap = {
                'DR': 'vas_105_acct-stage--qualification',
                'IP': 'vas_105_acct-stage--proposal',
                'CO': 'vas_105_acct-stage--won',
                'CL': 'vas_105_acct-stage--closed',
                'VO': 'vas_105_acct-stage--lost',
                'RE': 'vas_105_acct-stage--hold'
            };
            if (codeMap[code]) return codeMap[code];
            // Hash-based cycle for any completely unrecognised stage name — deterministic per stage
            var key = n || (code || '');
            var h = 0;
            for (var i = 0; i < key.length; i++) { h = (h * 31 + key.charCodeAt(i)) & 0xFFFF; }
            return 'vas_105_acct-stage--c' + ((h % 8) + 1);
        }
        function stageLabel(code) {
            var map = {
                'DR': 'Draft',
                'IP': 'In Progress',
                'CO': 'Won',
                'CL': 'Closed',
                'VO': 'Lost',
                'RE': 'Reversed'
            };
            return map[code] || (code || '—');
        }

        function renderOpps(el, data) {
            var allItems = (data && data.items) ? data.items : [];
            var sym      = (data && data.currencySymbol) || getSym();
            var prec     = (data && data.precision != null) ? parseInt(data.precision,10) : getPrec();

            var cntEl = document.getElementById(secId('opps') + '_cnt');
            if (cntEl) {
                // Exclude closed/lost/won/archived stages by both stage code and stage name keywords
                var CLOSED_CODES = { 'CO': 1, 'CL': 1, 'VO': 1, 'RE': 1 };
                var CLOSED_WORDS = /lost|archived|won|closed|reversed/i;
                var open = allItems.filter(function(o) {
                    if (CLOSED_CODES[o.stageCode]) return false;
                    var name = o.stageName || o.stageCode || '';
                    return !CLOSED_WORDS.test(name);
                }).length;
                cntEl.textContent = open > 0 ? (String(open) + ' ' + msg('Open')) : '';
            }

            if (!allItems.length) { el.innerHTML = emptyState('VAS_105_NoOpps'); return; }

            // Client-side pagination — slice the current page
            var total    = allItems.length;
            var pageStart = _oppsPage * OPP_PAGE_SIZE;
            var items    = allItems.slice(pageStart, pageStart + OPP_PAGE_SIZE);

            var SVG_OPP = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><circle cx="12" cy="12" r="6"/><circle cx="12" cy="12" r="2"/></svg>';
            var html =
                '<div class="vas_105_acct-tablehead" style="grid-template-columns:2.25em 1.7fr 1fr 0.9fr 1fr;gap:0.75em;margin-top:0.5em;">' +
                  '<span></span>' +
                  '<span>' + esc(msg('VIS_Name'))          + '</span>' +
                  '<span>' + esc(msg('VAS_105_Stage'))      + '</span>' +
                  '<span style="text-align:right;">' + esc(msg('VAS_Value'))         + '</span>' +
                  '<span style="text-align:right;">' + esc(msg('VAS_105_CloseDate')) + '</span>' +
                '</div>';

            for (var i = 0; i < items.length; i++) {
                var o = items[i];
                html +=
                    '<div class="vas_105_acct-clickrow" style="grid-template-columns:2.25em 1.7fr 1fr 0.9fr 1fr;gap:0.75em;" data-opp-idx="' + i + '">' +
                      '<span class="vas_105_acct-wicon vas_105_acct-wicon--blue">' + SVG_OPP + '</span>' +
                      '<span style="font-size:0.875em;font-weight:700;color:var(--acct-text);">' + esc(o.name || '') + '</span>' +
                      '<span><span class="vas_105_acct-stage ' + stageClass(o.stageCode, o.stageName) + '">' + esc(o.stageName || stageLabel(o.stageCode)) + '</span></span>' +
                      '<span style="font-size:0.875em;font-weight:700;color:var(--acct-text);text-align:right;">' + esc(fmtFull(toNum(o.value), sym, prec)) + '</span>' +
                      '<span style="font-size:0.8125em;color:var(--acct-text-2);text-align:right;">' + esc(fmtDate(o.closeDate)) + '</span>' +
                    '</div>';
            }

            html += buildSectionPager('opps', total, _oppsPage, OPP_PAGE_SIZE);
            el.innerHTML = html;

            // Wire row clicks (items is the current-page slice)
            var rows = el.querySelectorAll('.vas_105_acct-clickrow');
            for (var ri = 0; ri < rows.length; ri++) {
                (function(row, oppData) {
                    row.onclick = function () { openOppDetail(oppData); };
                })(rows[ri], items[ri]);
            }

            // Wire pagination buttons
            if (total > OPP_PAGE_SIZE) {
                var prevBtn = document.getElementById('vas_105_oppsprev_' + widgetID);
                var nextBtn = document.getElementById('vas_105_oppsnext_' + widgetID);
                var maxPage = Math.ceil(total / OPP_PAGE_SIZE) - 1;
                if (prevBtn) prevBtn.onclick = function () {
                    _oppsPage = Math.max(0, _oppsPage - 1);
                    renderOpps(el, data);
                };
                if (nextBtn) nextBtn.onclick = function () {
                    _oppsPage = Math.min(maxPage, _oppsPage + 1);
                    renderOpps(el, data);
                };
            }
        }

        // ── renderContracts ───────────────────────────────────────────────────
        function contractStatusClass(code) {
            // C_Contract Processed: Y=Completed, N=In Progress
            if (code === 'Y')                      return 'vas_105_acct-ctstatus--active';
            if (code === 'N')                      return 'vas_105_acct-ctstatus--draft';
            // VAS_ContractMaster VAS_Status: ARD=Approved(Active), DFT=Drafted, SFA=Sent For Approval, EXP=Expired, TRM=Terminated
            if (code === 'ARD')                    return 'vas_105_acct-ctstatus--active';
            if (code === 'DFT' || code === 'SFA')  return 'vas_105_acct-ctstatus--draft';
            return 'vas_105_acct-ctstatus--expired';
        }
        function contractStatusLabel(code) {
            // C_Contract Processed field
            if (code === 'Y') return msg('VAS_105_Completed');
            if (code === 'N') return msg('VAS_105_InProgress');
            // VAS_ContractMaster VAS_Status
            if (code === 'ARD') return msg('VAS_105_Approved');
            if (code === 'DFT') return msg('VIS_StatusDraft');
            if (code === 'SFA') return msg('VAS_105_SentForApproval');
            if (code === 'EXP') return msg('VIS_OverDue');
            if (code === 'TRM') return msg('VAS_105_Terminated');
            return code || '—';
        }

        function renderContracts(el, data) {
            var allItems = (data && data.items) ? data.items : [];
            var sym      = (data && data.currencySymbol) || getSym();
            var prec     = (data && data.precision != null) ? parseInt(data.precision,10) : getPrec();
            var today    = new Date().toISOString().split('T')[0];

            var cntEl = document.getElementById(secId('contracts') + '_cnt');
            if (cntEl) {
                // Completed/Active: C_Contract Processed=Y or VAS_ContractMaster ARD, not yet past end date
                var active = allItems.filter(function(c){
                    return (c.statusCode === 'Y' || c.statusCode === 'ARD') && !(c.endDate && c.endDate < today);
                }).length;
                // In Progress/Draft: C_Contract Processed=N or VAS_ContractMaster DFT
                var inProg = allItems.filter(function(c){ return c.statusCode === 'N' || c.statusCode === 'DFT'; }).length;
                var parts  = [];
                if (active) parts.push(String(active) + ' ' + msg('Active'));
                if (inProg) parts.push(String(inProg) + ' ' + msg('VAS_105_InProgress'));
                cntEl.textContent = parts.join(' · ');
            }

            if (!allItems.length) { el.innerHTML = emptyState('VAS_105_NoContracts'); return; }

            // Client-side pagination — slice the current page
            var total     = allItems.length;
            var pageStart = _contractsPage * CT_PAGE_SIZE;
            var items     = allItems.slice(pageStart, pageStart + CT_PAGE_SIZE);

            var SVG_CONTRACT = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14.5 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7.5L14.5 2z"/><polyline points="14 2 14 8 20 8"/></svg>';
            var html = '';
            for (var i = 0; i < items.length; i++) {
                var c = items[i];
                // A completed/active contract whose end date has passed is shown as Overdue.
                var isOverdue   = (c.statusCode === 'Y' || c.statusCode === 'ARD') && c.endDate && c.endDate < today;
                var statusCls   = isOverdue ? 'vas_105_acct-ctstatus--expired' : contractStatusClass(c.statusCode);
                var statusLabel = isOverdue ? msg('VIS_OverDue')                : contractStatusLabel(c.statusCode);
                var term = [fmtDate(c.startDate), c.endDate ? fmtDate(c.endDate) : 'Perpetual'].filter(Boolean).join(' – ');
                html +=
                    '<div class="vas_105_acct-ctrow" data-ct-idx="' + i + '">' +
                      '<span class="vas_105_acct-wicon vas_105_acct-wicon--teal">' + SVG_CONTRACT + '</span>' +
                      '<div style="min-width:0;flex:1;">' +
                        '<div style="display:flex;align-items:center;gap:0.5625em;">' +
                          '<span style="font-size:0.9375em;font-weight:700;color:var(--acct-text);">' + esc(c.name || c.contractNo || '') + '</span>' +
                          '<span class="vas_105_acct-ctstatus ' + statusCls + '">' + esc(statusLabel) + '</span>' +
                          '<span class="vas_105_acct-ctsource">' + esc(c.source === 'CC' ? msg('VAS_105_ServiceContract') : msg('VAS_105_Contract')) + '</span>' +
                        '</div>' +
                        '<div style="margin-top:0.25em;font-size:0.75em;color:var(--acct-text-2);">' + esc(c.typeCode || '') + ' · ' + esc(term) + '</div>' +
                        (c.productName ? '<div style="margin-top:0.1em;font-size:0.75em;color:var(--acct-text-2);">' + esc(c.productName) + '</div>' : (c.description ? '<div style="margin-top:0.1em;font-size:0.75em;color:var(--acct-text-2);">' + esc(c.description) + '</div>' : '')) +
                        (c.attributeDesc ? '<div style="margin-top:0.1em;font-size:0.75em;color:var(--acct-text-2);">' + esc(c.attributeDesc) + '</div>' : '') +
                      '</div>' +
                      '<div style="font-size:0.875em;font-weight:700;color:var(--acct-text);white-space:nowrap;">' + esc(fmtFull(toNum(c.value), sym, prec)) + '</div>' +
                    '</div>';
            }

            html += buildSectionPager('contracts', total, _contractsPage, CT_PAGE_SIZE);
            el.innerHTML = html;

            // Wire row clicks (items is the current-page slice)
            var rows = el.querySelectorAll('.vas_105_acct-ctrow');
            for (var ri = 0; ri < rows.length; ri++) {
                (function(row, ct) {
                    row.onclick = function () { openContractDetail(ct); };
                })(rows[ri], items[ri]);
            }

            // Wire pagination buttons
            if (total > CT_PAGE_SIZE) {
                var prevBtn = document.getElementById('vas_105_contractsprev_' + widgetID);
                var nextBtn = document.getElementById('vas_105_contractsnext_' + widgetID);
                var maxPage = Math.ceil(total / CT_PAGE_SIZE) - 1;
                if (prevBtn) prevBtn.onclick = function () {
                    _contractsPage = Math.max(0, _contractsPage - 1);
                    renderContracts(el, data);
                };
                if (nextBtn) nextBtn.onclick = function () {
                    _contractsPage = Math.min(maxPage, _contractsPage + 1);
                    renderContracts(el, data);
                };
            }
        }

        // ── renderTickets ─────────────────────────────────────────────────────
        function prioClass(code) {
            var c = String(code||'').toLowerCase();
            if (c === '1' || c === 'high')   return 'vas_105_acct-prio--high';
            if (c === '2' || c === 'medium') return 'vas_105_acct-prio--medium';
            return 'vas_105_acct-prio--low';
        }
        function prioLabel(code) {
            var map = {'1':'High','2':'Medium','3':'Low','High':'High','Medium':'Medium','Low':'Low'};
            return map[code] || code || '—';
        }
        function prioColor(code) {
            var c = String(code||'').toLowerCase();
            if (c === '1' || c === 'high')   return '#ED1C24';
            if (c === '2' || c === 'medium') return '#D78B10';
            return '#0083DA';
        }

        function renderTickets(el, data) {
            var items = (data && data.items) ? data.items : [];

            if (!items.length) { el.innerHTML = emptyState('VAS_105_NoTickets'); return; }

            var html = '';
            for (var i = 0; i < items.length; i++) {
                var t = items[i];
                var age = (t.ageDays != null) ? (String(t.ageDays) + 'd ' + msg('Open')) : '';
                var isAlert = t.isOverdue || t.isInactive;
                var dotColor = isAlert ? '#ED1C24' : prioColor(t.priorityCode);
                html +=
                    '<div class="vas_105_acct-tickrow' + (isAlert ? ' vas_105_acct-tickrow--alert' : '') + '" data-tick-idx="' + i + '">' +
                      '<span class="vas_105_acct-dot" style="background:' + dotColor + ';"></span>' +
                      '<span style="font-size:0.8125em;color:var(--acct-muted);width:4.875em;flex-shrink:0;">' + esc(t.ticketNo || '') + '</span>' +
                      '<span style="flex:1;min-width:0;font-size:0.8125em;color:var(--acct-text);overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">' + esc(t.subject || '') + '</span>' +
                      '<span class="vas_105_acct-prio ' + prioClass(t.priorityCode) + '">' + esc(t.priorityName || prioLabel(t.priorityCode)) + '</span>' +
                      '<span style="font-size:0.75em;color:var(--acct-text-2);width:4.375em;text-align:right;flex-shrink:0;">' + esc(age) + '</span>' +
                    '</div>';
            }
            el.innerHTML = html;

            var rows = el.querySelectorAll('.vas_105_acct-tickrow');
            for (var ri = 0; ri < rows.length; ri++) {
                (function(row, t) {
                    row.onclick = function () { openTicketDetail(t); };
                })(rows[ri], items[ri]);
            }
        }

        // ── renderOrders ──────────────────────────────────────────────────────
        function orderStatusClass(code) {
            if (code === 'CO') return 'vas_105_acct-odstatus--fulfilled';
            if (code === 'VO') return 'vas_105_acct-odstatus--cancelled';
            return 'vas_105_acct-odstatus--processing';
        }
        function orderStatusLabel(code) {
            if (code === 'CO') return 'Fulfilled';
            if (code === 'VO') return 'Cancelled';
            if (code === 'DR') return 'Draft';
            if (code === 'IP') return 'Processing';
            return code || '—';
        }

        function renderOrders(el, data) {
            var items = (data && data.items) ? data.items : [];
            var total = (data && data.total != null) ? data.total : items.length;
            var sym   = (data && data.currencySymbol) || getSym();
            var prec  = (data && data.precision != null) ? parseInt(data.precision,10) : getPrec();

            if (!items.length) { el.innerHTML = emptyState('VAS_105_NoOrders'); return; }

            var SVG_ORDER = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"/><polyline points="3.27 6.96 12 12.01 20.73 6.96"/><line x1="12" y1="22.08" x2="12" y2="12"/></svg>';
            var html =
                '<div class="vas_105_acct-tablehead" style="grid-template-columns:2.25em 1fr 1.2fr 1fr 0.8fr;gap:0.75em;margin-top:0.5em;">' +
                  '<span></span>' +
                  '<span>' + esc(msg('VAS_105_OrderNo'))  + '</span>' +
                  '<span>' + esc(msg('VAS_105_Items'))    + '</span>' +
                  '<span style="text-align:right;">' + esc(msg('Amount')) + '</span>' +
                  '<span style="text-align:right;">' + esc(msg('Status')) + '</span>' +
                '</div>';

            for (var i = 0; i < items.length; i++) {
                var o = items[i];
                html +=
                    '<div class="vas_105_acct-clickrow" style="grid-template-columns:2.25em 1fr 1.2fr 1fr 0.8fr;gap:0.75em;" data-ord-idx="' + i + '">' +
                      '<span class="vas_105_acct-wicon vas_105_acct-wicon--blue">' + SVG_ORDER + '</span>' +
                      '<div style="display:flex;flex-direction:column;gap:0.1em;">' +
                        '<span style="font-size:0.8125em;font-weight:700;color:var(--acct-text);">' + esc(o.orderNo || '') + '</span>' +
                        '<span style="font-size:0.75em;color:var(--acct-text-2);">' + esc(fmtDate(o.orderDate)) + '</span>' +
                      '</div>' +
                      '<span style="font-size:0.8125em;color:var(--acct-text);overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">' + esc(o.items || '') + '</span>' +
                      '<span style="font-size:0.875em;font-weight:700;color:var(--acct-text);text-align:right;">' + esc(fmtFull(toNum(o.amount), sym, prec)) + '</span>' +
                      '<span style="text-align:right;"><span class="vas_105_acct-odstatus ' + orderStatusClass(o.statusCode) + '">' + esc(orderStatusLabel(o.statusCode)) + '</span></span>' +
                    '</div>';
            }

            if (total > PAGE_SIZE_INLINE) {
                html += buildSectionPager('ord', total, Math.floor(_ordersOffset / PAGE_SIZE_INLINE), PAGE_SIZE_INLINE);
            }

            el.innerHTML = html;

            var rows = el.querySelectorAll('.vas_105_acct-clickrow');
            for (var ri = 0; ri < rows.length; ri++) {
                (function(row, o) { row.onclick = function () { openOrderDetail(o); }; })(rows[ri], items[ri]);
            }

            if (total > PAGE_SIZE_INLINE) {
                var prevBtn = document.getElementById('vas_105_ordprev_' + widgetID);
                var nextBtn = document.getElementById('vas_105_ordnext_' + widgetID);
                if (prevBtn) prevBtn.onclick = function () {
                    _ordersOffset = Math.max(0, _ordersOffset - PAGE_SIZE_INLINE);
                    fetchSection('orders', 'GetOrders', { pageOffset: _ordersOffset, pageSize: PAGE_SIZE_INLINE }, null);
                };
                if (nextBtn) nextBtn.onclick = function () {
                    _ordersOffset = _ordersOffset + PAGE_SIZE_INLINE;
                    fetchSection('orders', 'GetOrders', { pageOffset: _ordersOffset, pageSize: PAGE_SIZE_INLINE }, null);
                };
            }
        }

        // ── renderInvoices ────────────────────────────────────────────────────
        function ivStatusClass(code) {
            var map = { 'Paid':'vas_105_acct-ivstatus--paid', 'Overdue':'vas_105_acct-ivstatus--overdue',
                        'Partial':'vas_105_acct-ivstatus--partial' };
            return map[code] || 'vas_105_acct-ivstatus--open';
        }

        function renderInvoices(el, data) {
            var items = (data && data.items) ? data.items : [];
            var total = (data && data.total != null) ? data.total : items.length;
            var sym   = (data && data.currencySymbol) || getSym();
            var prec  = (data && data.precision != null) ? parseInt(data.precision,10) : getPrec();

            if (!items.length) { el.innerHTML = emptyState('VAS_105_NoInvoices'); return; }

            var SVG_INVOICE = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/><polyline points="10 9 9 9 8 9"/></svg>';
            var html =
                '<div class="vas_105_acct-tablehead" style="grid-template-columns:2.25em 1fr 0.9fr 1fr 0.8fr;gap:0.75em;margin-top:0.5em;">' +
                  '<span></span>' +
                  '<span>' + esc(msg('InvoiceNo')) + '</span>' +
                  '<span style="text-align:right;">' + esc(msg('Amount'))  + '</span>' +
                  '<span style="text-align:right;">' + esc(msg('DueDate')) + '</span>' +
                  '<span style="text-align:right;">' + esc(msg('Status'))  + '</span>' +
                '</div>';

            for (var i = 0; i < items.length; i++) {
                var inv = items[i];
                html +=
                    '<div class="vas_105_acct-clickrow" style="grid-template-columns:2.25em 1fr 0.9fr 1fr 0.8fr;gap:0.75em;" data-inv-idx="' + i + '">' +
                      '<span class="vas_105_acct-wicon vas_105_acct-wicon--amber">' + SVG_INVOICE + '</span>' +
                      '<div style="display:flex;flex-direction:column;gap:0.1em;">' +
                        '<span style="font-size:0.8125em;font-weight:700;color:var(--acct-text);">' + esc(inv.invoiceNo || '') + '</span>' +
                        '<span style="font-size:0.75em;color:var(--acct-text-2);">' + esc(fmtDate(inv.invoiceDate)) + '</span>' +
                      '</div>' +
                      '<span style="font-size:0.875em;font-weight:700;color:var(--acct-text);text-align:right;">' + esc(fmtFull(toNum(inv.amount), sym, prec)) + '</span>' +
                      '<span style="font-size:0.8125em;color:var(--acct-text-2);text-align:right;">' + esc(fmtDate(inv.dueDate)) + '</span>' +
                      '<span style="text-align:right;"><span class="vas_105_acct-ivstatus ' + ivStatusClass(inv.payStatus) + '">' + esc(inv.payStatus || 'Open') + '</span></span>' +
                    '</div>';
            }

            if (total > PAGE_SIZE_INLINE) {
                html += buildSectionPager('inv', total, Math.floor(_invoicesOffset / PAGE_SIZE_INLINE), PAGE_SIZE_INLINE);
            }

            el.innerHTML = html;

            var rows = el.querySelectorAll('.vas_105_acct-clickrow');
            for (var ri = 0; ri < rows.length; ri++) {
                (function(row, inv) { row.onclick = function () { openInvoiceDetail(inv); }; })(rows[ri], items[ri]);
            }

            if (total > PAGE_SIZE_INLINE) {
                var prevBtn = document.getElementById('vas_105_invprev_' + widgetID);
                var nextBtn = document.getElementById('vas_105_invnext_' + widgetID);
                if (prevBtn) prevBtn.onclick = function () {
                    _invoicesOffset = Math.max(0, _invoicesOffset - PAGE_SIZE_INLINE);
                    fetchSection('invoices', 'GetInvoices', { pageOffset: _invoicesOffset, pageSize: PAGE_SIZE_INLINE }, null);
                };
                if (nextBtn) nextBtn.onclick = function () {
                    _invoicesOffset = _invoicesOffset + PAGE_SIZE_INLINE;
                    fetchSection('invoices', 'GetInvoices', { pageOffset: _invoicesOffset, pageSize: PAGE_SIZE_INLINE }, null);
                };
            }
        }

        // ── renderProjects ────────────────────────────────────────────────────
        function projStatusClass(code) {
            if (code === 'OP' || code === 'AC') return 'vas_105_acct-projstatus--active';
            if (code === 'PL')                  return 'vas_105_acct-projstatus--planning';
            if (code === 'OH')                  return 'vas_105_acct-projstatus--hold';
            if (code === 'CL' || code === 'CO') return 'vas_105_acct-projstatus--complete';
            return 'vas_105_acct-projstatus--planning';
        }
        function projStatusLabel(code) {
            var map = {'OP':'Active','AC':'Active','PL':'Planning','OH':'On Hold','CL':'Closed','CO':'Complete'};
            return map[code] || code || '—';
        }

        function renderProjects(el, data) {
            var items = (data && data.items) ? data.items : [];

            if (!items.length) { el.innerHTML = emptyState('VAS_105_NoProjects'); return; }

            var SVG_PROJECT = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="2" y="7" width="20" height="14" rx="2" ry="2"/><path d="M16 21V5a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v16"/></svg>';
            var html =
                '<div class="vas_105_acct-tablehead" style="grid-template-columns:2.25em 2fr 1fr 0.8fr;gap:0.75em;margin-top:0.5em;">' +
                  '<span></span>' +
                  '<span>' + esc(msg('Project')) + '</span>' +
                  '<span>' + esc(msg('Status'))  + '</span>' +
                  '<span style="text-align:right;">' + esc(msg('VAS_105_Due')) + '</span>' +
                '</div>';

            for (var i = 0; i < items.length; i++) {
                var p = items[i];
                html +=
                    '<div class="vas_105_acct-projrow" style="grid-template-columns:2.25em 2fr 1fr 0.8fr;gap:0.75em;" data-proj-idx="' + i + '">' +
                      '<span class="vas_105_acct-wicon vas_105_acct-wicon--violet">' + SVG_PROJECT + '</span>' +
                      '<span style="font-size:0.875em;font-weight:700;color:var(--acct-text);">' + esc(p.name || '') + '</span>' +
                      '<span><span class="vas_105_acct-projstatus ' + projStatusClass(p.statusCode) + '">' + esc(projStatusLabel(p.statusCode)) + '</span></span>' +
                      '<span style="font-size:0.8125em;color:var(--acct-text-2);text-align:right;">' + esc(fmtDate(p.due)) + '</span>' +
                    '</div>';
            }
            el.innerHTML = html;

            var rows = el.querySelectorAll('.vas_105_acct-projrow');
            for (var ri = 0; ri < rows.length; ri++) {
                (function(row, p) { row.onclick = function () { openProjectDetail(p); }; })(rows[ri], items[ri]);
            }
        }

        // ── Tasks helpers ─────────────────────────────────────────────────────

        function priorityBadge(label, code) {
            var text = label || code;
            if (!text) return '';
            var u = String(text).toUpperCase();
            var cls, dot;
            if (u.indexOf('URGENT') >= 0 || u.indexOf('CRITICAL') >= 0) {
                cls = 'vas_105_t-danger'; dot = '#ED1C24';
            } else if (u.indexOf('HIGH') >= 0) {
                cls = 'vas_105_t-warn';   dot = '#D78B10';
            } else if (u.indexOf('MED') >= 0 || u.indexOf('MEDIUM') >= 0) {
                cls = 'vas_105_t-info';   dot = '#0083DA';
            } else {
                cls = 'vas_105_t-neutral'; dot = '#94A3B8';
            }
            return '<span class="vas_105_tag ' + cls + '">' +
                   '<span style="width:0.45em;height:0.45em;border-radius:50%;background:' + dot + ';flex-shrink:0;display:inline-block;margin-right:0.3em;"></span>' +
                   esc(text) + '</span>';
        }

        function initials2(name) {
            if (!name) return 'NA';
            var parts = String(name).trim().split(/\s+/);
            if (parts.length >= 2) return (parts[0].charAt(0) + parts[1].charAt(0)).toUpperCase();
            var s = parts[0];
            return (s.charAt(0) + (s.charAt(1) || '')).toUpperCase();
        }

        function fmtDateLong(s) {
            if (!s) return '';
            var p = String(s).split('-');
            if (p.length < 3) return s;
            var months = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
            var mon = months[parseInt(p[1], 10) - 1] || p[1];
            return p[2] + ' ' + mon + ' ' + p[0];
        }

        function renderTaskList(tasks) {
            if (!tasks || tasks.length === 0) {
                return '<div class="vas_105_acct-empty">' + esc(msg('VAS_105_NoTasks')) + '</div>';
            }
            var today = new Date().toISOString().split('T')[0];
            var html = '';
            for (var i = 0; i < tasks.length; i++) {
                var t       = tasks[i];
                var done    = t.isClosed === true || t.isClosed === 'true';
                var overdue = !done && t.dueDate && t.dueDate < today;
                var titleCls = 'vas_105_ttitle' + (done ? ' done' : '');
                var pct     = done ? 100 : (t.completionPct ? (parseInt(t.completionPct, 10) || 0) : 0);

                var avHtml = '';
                if (t.assignee) {
                    avHtml = '<span class="vas_105_tav">' + esc(initials2(t.assignee)) + '</span>' + esc(t.assignee);
                }

                var dueHtml = '';
                if (t.dueDate) {
                    var dueText = esc(msg('VAS_105_Due')) + ' ' + esc(fmtDateLong(t.dueDate));
                    if (overdue) dueText += ' &middot; ' + esc(msg('VIS_OverDue'));
                    dueHtml = '<span' + (overdue ? ' class="vas_105_over"' : '') + '>' + dueText + '</span>';
                }

                var metaItems = [];
                if (avHtml)  metaItems.push(avHtml);
                if (dueHtml) metaItems.push(dueHtml);
                metaItems.push('<span>' + pct + '%</span>');
                var metaHtml = metaItems.join('<span class="vas_105_dot">&middot;</span>');

                html += [
                    '<div class="vas_105_taskrow">',
                    '  <input type="checkbox" class="vas_105_tchk" data-task-id="', t.id, '"',
                             done ? ' checked' : '',
                    '  data-is-closed="', String(done), '"',
                    '  title="', esc(msg('VAS_105_MarkDone')), '">',
                    '  <div class="vas_105_taskrow-main" data-task-id="', t.id, '">',
                    '    <div class="', titleCls, '">', esc(t.title || '—'), '</div>',
                    '    <div class="vas_105_taskrow-meta">', metaHtml, '</div>',
                    '  </div>',
                    '  ', priorityBadge(t.priorityLabel, t.priorityCode),
                    '</div>'
                ].join('');
            }
            return html;
        }

        function renderTasks(data) {
            var items    = (data && data.items) ? data.items : [];
            var upcoming = items.filter(function (t) { return !(t.isClosed === true || t.isClosed === 'true'); });
            var previous = items.filter(function (t) { return   t.isClosed === true || t.isClosed === 'true';  });
            var cntEl = document.getElementById(secId('tasks') + '_cnt');
            if (cntEl) cntEl.textContent = upcoming.length > 0 ? (String(upcoming.length) + ' ' + msg('Open')) : '';
            _bpSectionCounts['tasks'] = items.length;
            applyAddBtnState();
            return renderTaskList(taskFilter === 'previous' ? previous : upcoming);
        }

        function loadTasks() {
            fetchSection('tasks', 'GetTasks', {}, null);
        }

        // ── renderEngagement ─────────────────────────────────────────────────
        function renderEngagement(el, data) {
            var counts = (data && data.counts) ? data.counts : {};
            var items  = (data && data.items)  ? data.items : [];
            _bpSectionCounts['timeline'] = items.length;
            applyAddBtnState();

            // Inline SVG icons
            var SVG_LIST =
                '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                '<line x1="8" y1="6" x2="21" y2="6"/><line x1="8" y1="12" x2="21" y2="12"/><line x1="8" y1="18" x2="21" y2="18"/>' +
                '<line x1="3" y1="6" x2="3.01" y2="6"/><line x1="3" y1="12" x2="3.01" y2="12"/><line x1="3" y1="18" x2="3.01" y2="18"/></svg>';
            var SVG_PHONE =
                '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                '<path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07A19.5 19.5 0 0 1 4.69 13a19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 3.77 2h3a2 2 0 0 1 2 1.72c.127.96.361 1.903.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l.91-.91a2 2 0 0 1 2.11-.45c.907.339 1.85.573 2.81.7A2 2 0 0 1 22 17z"/></svg>';
            var SVG_CALENDAR =
                '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                '<rect x="3" y="4" width="18" height="18" rx="2" ry="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/></svg>';
            var SVG_MAIL =
                '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                '<rect x="2" y="4" width="20" height="16" rx="2"/><path d="m22 7-8.97 5.7a1.94 1.94 0 0 1-2.06 0L2 7"/></svg>';
            var SVG_NOTE =
                '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                '<path d="M14.5 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7.5L14.5 2z"/><polyline points="14 2 14 8 20 8"/></svg>';
            var SVG_WHATSAPP =
                '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">' +
                '<path d="M21 11.5a8.38 8.38 0 0 1-.9 3.8 8.5 8.5 0 0 1-7.6 4.7 8.38 8.38 0 0 1-3.8-.9L3 21l1.9-5.7a8.38 8.38 0 0 1-.9-3.8 8.5 8.5 0 0 1 4.7-7.6 8.38 8.38 0 0 1 3.8-.9h.5a8.48 8.48 0 0 1 8 8v.5z"/></svg>';
            var SVG_TRANSCRIPT =
                '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                '<path d="M14.5 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7.5L14.5 2z"/><polyline points="14 2 14 8 20 8"/>' +
                '<line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/><line x1="10" y1="9" x2="8" y2="9"/></svg>';
            var SVG_REPLY =
                '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                '<polyline points="9 17 4 12 9 7"/><path d="M20 18v-2a4 4 0 0 0-4-4H4"/></svg>';
            var SVG_SEND =
                '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                '<line x1="22" y1="2" x2="11" y2="13"/><polygon points="22 2 15 22 11 13 2 9 22 2"/></svg>';

            var BADGE = {
                'NOTE':    { bg:'#FEF3C7', color:'#92400E', labelKey:'Note'    },
                'MEETING': { bg:'#EDE9FE', color:'#5B21B6', labelKey:'VAS_105_Meeting' },
                'EMAIL':   { bg:'#DBEAFE', color:'#1D4ED8', labelKey:'EMail'             },
                'CALL':    { bg:'#D1FAE5', color:'#065F46', labelKey:'VAS_105_Call'    },
                'CHAT':    { bg:'#D1FAE5', color:'#065F46', labelKey:'VAS_105_Chat'    }
            };
            var DOT_COLOR = {
                'NOTE':'#F59E0B', 'MEETING':'#7C3AED', 'EMAIL':'#2563EB', 'CALL':'#059669', 'CHAT':'#059669'
            };
            var TYPE_SVG = {
                'NOTE': SVG_NOTE, 'MEETING': SVG_CALENDAR, 'EMAIL': SVG_MAIL, 'CALL': SVG_PHONE, 'CHAT': SVG_WHATSAPP
            };

            var inCnt = 0, outCnt = 0;
            for (var i = 0; i < items.length; i++) {
                if (items[i].touchType === 'EMAIL') { if (items[i].direction === 'in') inCnt++; else outCnt++; }
            }
            var emailSub = inCnt + ' in · ' + outCnt + ' out';

            var totalMeetingMins  = counts.totalMeetingMins  || 0;
            var meetingAttendees  = counts.meetingAttendees   || 0;
            var totalCallMins     = counts.totalCallMins      || 0;
            var connectedCalls    = counts.connectedCalls     || 0;

            var mtgParts = [];
            if (totalMeetingMins > 0) mtgParts.push(totalMeetingMins + 'm');
            if (meetingAttendees > 0) mtgParts.push(meetingAttendees + ' ' + msg('VAS_105_Attended'));
            var mtgSub = mtgParts.length ? mtgParts.join(' · ') : msg('Transcript');

            var callParts = [];
            if (totalCallMins > 0) callParts.push(totalCallMins + 'm');
            if (connectedCalls > 0) callParts.push(connectedCalls + ' ' + msg('VAS_105_Connected'));
            var callSub = callParts.length ? callParts.join(' · ') : msg('VAS_105_Connected');

            function statCard(svgIcon, count, labelKey, sub, action) {
                var cls   = 'vas_105_eng-statcard' + (action ? ' vas_105_eng-statcard--clickable' : '');
                var attrs = action ? ' data-action="' + action + '"' : '';
                return [
                    '<div class="', cls, '"', attrs, '>',
                    '  <div class="vas_105_eng-statcard-icon">', svgIcon, '</div>',
                    '  <div class="vas_105_eng-statcard-count">', count, '</div>',
                    '  <div class="vas_105_eng-statcard-label">', esc(msg(labelKey)), '</div>',
                    '  <div class="vas_105_eng-statcard-sub">', esc(sub), '</div>',
                    '</div>'
                ].join('');
            }

            var html = '<div class="vas_105_eng-statstrip">';
            html += statCard(SVG_LIST,     counts.total    || 0, 'VAS_105_AllTouches', msg('VAS_105_Last30Days'));
            html += statCard(SVG_PHONE,    counts.calls    || 0, 'VAS_105_Calls',      callSub);
            html += statCard(SVG_CALENDAR, counts.meetings || 0, 'VAS_105_Meetings',   mtgSub);
            html += statCard(SVG_MAIL,     counts.emails   || 0, 'VAS_105_Emails',     emailSub);
            html += statCard(SVG_WHATSAPP, counts.chat     || 0, 'VAS_105_WhatsApp',   msg('VAS_105_WhatsApp'), 'openChat');
            html += '</div>';

            if (items.length === 0) {
                html += '<div class="vas_105_acct-empty">' + esc(msg('VAS_105_NoEngagement')) + '</div>';
                el.innerHTML = html;
                return;
            }

            // The timeline pages client-side: the stat strip above still counts
            // every touch, only the rail below it is limited to one page.
            var engPageCount = Math.max(1, Math.ceil(items.length / ENGAGEMENT_PER_PAGE));
            if (_engPage >= engPageCount) _engPage = engPageCount - 1;
            if (_engPage < 0) _engPage = 0;
            var engStart = _engPage * ENGAGEMENT_PER_PAGE;
            var engEnd   = Math.min(items.length, engStart + ENGAGEMENT_PER_PAGE);

            html += '<div class="vas_105_eng-tl-wrap">';
            for (var i = engStart; i < engEnd; i++) {
                var item   = items[i];
                var bCfg   = BADGE[item.touchType]    || BADGE['NOTE'];
                var dotClr = DOT_COLOR[item.touchType] || '#94A3B8';
                var icon   = TYPE_SVG[item.touchType]  || SVG_NOTE;
                // The connector stops at the last entry ON THIS PAGE, so the rail
                // never trails off below the final card.
                var isLast = (i === engEnd - 1);

                var titleText = item.title || '';
                if (item.touchType === 'NOTE') {
                    titleText = (item.title || msg('Notes')) + (item.who ? ' · ' + item.who : '');
                } else if (item.touchType === 'EMAIL') {
                    var emailPrefix = item.direction === 'in' ? msg('From') : msg('To');
                    titleText = (item.title || '') + (item.who ? ' · ' + emailPrefix + ' ' + item.who : '');
                } else if (item.touchType === 'CHAT') {
                    titleText = msg('VAS_105_WhatsApp') + (item.who ? ' · ' + msg('VAS_105_With') + ' ' + item.who : '');
                } else if (item.touchType === 'MEETING') {
                    var durStr = '';
                    if (item.durationMins > 0) {
                        var dh = Math.floor(item.durationMins / 60), dm = item.durationMins % 60;
                        durStr = dh > 0 ? (dh + 'h' + (dm > 0 ? ' ' + dm + 'm' : '')) : (dm + 'm');
                    }
                    titleText = (item.title || '')
                        + (item.location ? ' · ' + item.location : '')
                        + (durStr        ? ' · ' + durStr        : '');
                }

                var dirBadge = '';
                if (item.direction === 'in')  dirBadge = '<span class="vas_105_eng-dir-in">'  + esc(msg('VAS_105_Incoming')) + '</span>';
                if (item.direction === 'out') dirBadge = '<span class="vas_105_eng-dir-out">' + esc(msg('VAS_105_Outgoing')) + '</span>';

                var metaHtml = '';
                if (item.touchType === 'MEETING') {
                    var mtParts = [];
                    if (item.who) mtParts.push(esc(item.who));
                    if (item.hasTranscript) mtParts.push('<span class="vas_105_eng-meta-badge">' + SVG_TRANSCRIPT + ' ' + esc(msg('Transcript')) + '</span>');
                    metaHtml = mtParts.join('&nbsp;&nbsp;');
                } else if (item.touchType === 'EMAIL') {
                    var emParts = [];
                    if (item.who) emParts.push(esc(item.who));
                    if (item.direction === 'in')  emParts.push('<span class="vas_105_eng-meta-badge">' + SVG_REPLY + ' ' + esc(msg('VAS_105_ReplyReceived')) + '</span>');
                    if (item.direction === 'out') emParts.push('<span class="vas_105_eng-meta-badge">' + SVG_SEND  + ' ' + esc(msg('Sent')) + '</span>');
                    metaHtml = emParts.join('&nbsp;&nbsp;');
                } else if (item.touchType === 'CALL' || item.touchType === 'CHAT') {
                    metaHtml = item.who ? esc(item.who) : '';
                }

                var isMeeting  = (item.touchType === 'MEETING');
                var isNote     = (item.touchType === 'NOTE');
                var isEmail    = (item.touchType === 'EMAIL');
                var isChat     = (item.touchType === 'CHAT');
                var meetingId  = isMeeting ? (item.meetingId || 0) : 0;
                var noteId     = isNote     ? (item.noteId    || 0) : 0;
                var emailId    = isEmail    ? (item.emailId   || 0) : 0;
                var topicId    = isChat     ? (item.topicId   || 0) : 0;
                var isClickable = meetingId > 0 || noteId > 0 || emailId > 0 || topicId > 0;
                var cardClass  = 'vas_105_eng-tl-card' + (isClickable ? ' vas_105_eng-tl-card--clickable' : '');
                var cardAttr   = meetingId > 0 ? ' data-meeting-id="' + meetingId + '"'
                               : noteId     > 0 ? ' data-note-id="'    + noteId    + '"'
                               : emailId    > 0 ? ' data-email-id="'   + emailId   + '"'
                               : topicId    > 0 ? ' data-chat-topic-id="' + topicId + '"'
                               : '';

                html += [
                    '<div class="vas_105_eng-tl-entry">',
                    '  <div class="vas_105_eng-tl-gutter">',
                    '    <div class="vas_105_eng-tl-dot" style="background:', dotClr, '"></div>',
                    isLast ? '' : '<div class="vas_105_eng-tl-line"></div>',
                    '  </div>',
                    '  <div class="', cardClass, '"', cardAttr, '>',
                    '    <div class="vas_105_eng-tl-card-top">',
                    '      <span class="vas_105_eng-badge" style="background:', bCfg.bg, ';color:', bCfg.color, ';">',
                    '        <span style="display:inline-flex;align-items:center;gap:0.2em;vertical-align:middle;">', icon, ' ', esc(msg(bCfg.labelKey)), '</span>',
                    '      </span>',
                    dirBadge,
                    '      <span class="vas_105_eng-ts">', esc(fmtEngTs(item.whenTs)), '</span>',
                    '    </div>',
                    '    <div class="vas_105_eng-tl-title">', esc(titleText), '</div>',
                    item.preview ? '<div class="vas_105_eng-tl-preview">' + (item.touchType === 'CHAT' ? '&ldquo;' + esc(item.preview) + '&rdquo;' : esc(item.preview)) + '</div>' : '',
                    metaHtml ? '<div class="vas_105_eng-tl-meta">' + metaHtml + '</div>' : '',
                    '  </div>',
                    '</div>'
                ].join('');
            }
            html += '</div>';

            // Pager — same shell the Orders / Invoices panels use, but the pages
            // are cut from the payload already in hand rather than re-fetched.
            if (engPageCount > 1) {
                var engChevL = '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="#586575" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"></polyline></svg>';
                var engChevR = '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="#586575" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"></polyline></svg>';
                html +=
                    '<div style="display:flex;align-items:center;justify-content:space-between;margin-top:0.625em;">' +
                      '<span class="of-lu-count" style="font-size:0.8125em;color:var(--acct-text-2);">' +
                        esc(msg('VAS_040_Showing')) + ' <b>' + (engStart + 1) + '&ndash;' + engEnd + '</b> ' +
                        esc(msg('of')) + ' <b>' + items.length + '</b>' +
                      '</span>' +
                      '<nav class="of-lu-pager-controls" role="navigation">' +
                        '<button type="button" class="of-lu-pager-btn of-lu-prev-btn" data-eng-page="' + (_engPage - 1) + '"' +
                          (_engPage <= 0 ? ' disabled' : '') + '>' + engChevL + '</button>' +
                        '<button type="button" class="of-lu-pager-btn of-lu-next-btn" data-eng-page="' + (_engPage + 1) + '"' +
                          (_engPage >= engPageCount - 1 ? ' disabled' : '') + '>' + engChevR + '</button>' +
                      '</nav>' +
                    '</div>';
            }

            el.innerHTML = html;

            // Bound here rather than delegated on $root: the pager has to re-enter
            // this same function with the same el / data to repaint the rail.
            if (engPageCount > 1) {
                $(el).find('[data-eng-page]').on('click', function (e) {
                    e.preventDefault();
                    e.stopPropagation();
                    var p = parseInt(this.getAttribute('data-eng-page'), 10);
                    if (isNaN(p) || p < 0 || p > engPageCount - 1) return;
                    _engPage = p;
                    renderEngagement(el, data);
                });
            }
        }

        // ── renderNotes ───────────────────────────────────────────────────────
        function renderNotes(el, data) {
            // 🔶 Extension — graceful empty state + post box
            var items     = (data && data.items)     ? data.items     : [];
            var available = (data && data.available !== false);

            var listHtml = '';
            if (!items.length) {
                listHtml = '<div class="vas_105_acct-empty">' + esc(msg('VAS_105_NoNotes')) + '</div>';
            } else {
                for (var i = 0; i < items.length; i++) {
                    var n = items[i];
                    if (n.isPinned === 'Y' || n.isPinned === true) {
                        listHtml +=
                            '<div class="vas_105_acct-pinned" style="margin-bottom:0.625em;">' +
                              '<div style="display:flex;align-items:center;gap:0.5em;">' +
                                '<span class="vas_105_acct-pinned__tag">PINNED</span>' +
                                '<span style="font-size:0.75em;color:var(--acct-text-2);">' + esc(n.author||'') + ' · ' + esc(n.created||'') + '</span>' +
                              '</div>' +
                              '<div style="margin-top:0.375em;font-size:0.8125em;color:var(--acct-text);line-height:1.5;">' + esc(n.noteText||'') + '</div>' +
                            '</div>';
                    } else {
                        listHtml +=
                            '<div class="vas_105_acct-noteitem" style="margin-bottom:0.5em;">' +
                              '<div class="vas_105_acct-noteitem__meta">' + esc(n.author||'') + ' · ' + esc(n.created||'') + '</div>' +
                              '<div class="vas_105_acct-noteitem__text">' + esc(n.noteText||'') + '</div>' +
                            '</div>';
                    }
                }
            }

            var postBoxHtml = available
                ?
                    '<div class="vas_105_acct-notebox">' +
                      '<textarea id="vas_105_note_input_' + widgetID + '" placeholder="' + esc(msg('VAS_105_AddNote')) + '" aria-label="' + esc(msg('VAS_105_AddNote')) + '"></textarea>' +
                      '<div class="vas_105_acct-notebox__foot">' +
                        '<span class="vas_105_acct-notebox__hint">' + esc(msg('VAS_105_VisibleToTeam')) + '</span>' +
                        '<button class="vas_105_acct-btn vas_105_acct-btn--primary" id="vas_105_note_post_' + widgetID + '">' +
                          '<i class="ti ti-send" style="font-size:0.9375em;"></i>' + esc(msg('VAS_105_PostNote')) +
                        '</button>' +
                      '</div>' +
                    '</div>'
                : '<div class="vas_105_acct-empty" style="font-size:0.75em;">' + esc(msg('VAS_105_ExtNotAvail')) + '</div>';

            el.innerHTML = listHtml + postBoxHtml;

            // Wire Post Note button
            var btnPost = document.getElementById('vas_105_note_post_' + widgetID);
            if (btnPost) {
                btnPost.onclick = function () {
                    var inp = document.getElementById('vas_105_note_input_' + widgetID);
                    var txt = inp ? $.trim(inp.value) : '';
                    if (!txt) return;
                    submitNote(txt, inp);
                };
            }
        }

        // ── Detail modals ─────────────────────────────────────────────────────
        function detailRow(label, value) {
            if (!value) return '';
            return '<div style="display:flex;justify-content:space-between;padding:0.625em 0;border-bottom:0.0625em solid #EDF2F6;">' +
                   '<span style="font-size:1em;color:#5F7283;">' + esc(label) + '</span>' +
                   '<span style="font-size:1em;color:#102C3F;">' + esc(value) + '</span></div>';
        }

        function openOppDetail(o) {
            var sym  = getSym(); var prec = getPrec();
            var body = detailRow(msg('VAS_105_Stage'), o.stageName || stageLabel(o.stageCode)) +
                       detailRow(msg('VAS_Value'), fmtFull(toNum(o.value), sym, prec)) +
                       detailRow(msg('VAS_105_CloseDate'), fmtDate(o.closeDate)) +
                       detailRow(msg('VAS_105_Probability'), o.probability ? (String(o.probability) + '%') : '') +
                       detailRow(msg('VAS_105_Owner'), o.owner) +
                       detailRow(msg('VAS_105_OppCode'), o.oppCode);
            var foot = '<button class="vas_105_acct-btn vas_105_acct-btn--secondary" onclick="(function(){document.getElementById(\'vas_105_overlay_' + widgetID + '\').classList.remove(\'vas_105_acct-overlay--open\');})();">' + esc(msg('VIS_Close')) + '</button>';
            showModal(o.name || msg('Opportunity'), msg('VAS_105_OpportunityDetail'), body, foot, false);
        }

        function openContractDetail(c) {
            var sym  = getSym(); var prec = getPrec();
            var descHtml = c.description
                ? '<div style="margin-top:1em;padding:0.875em;background:#F8FAFC;border:0.0625em solid #E4EDF4;border-radius:0.5em;font-size:0.875em;line-height:1.65;color:#102C3F;">' + esc(c.description) + '</div>'
                : '';
            var body = detailRow(msg('VAS_105_ContractNo'), c.contractNo) +
                       detailRow(msg('Type'), c.typeCode) +
                       detailRow(msg('Status'), contractStatusLabel(c.statusCode)) +
                       detailRow(msg('StartDate'), fmtDate(c.startDate)) +
                       detailRow(msg('EndDate'), c.endDate ? fmtDate(c.endDate) : 'Perpetual') +
                       detailRow(msg('VAS_Value'), fmtFull(toNum(c.value), sym, prec)) +
                       detailRow(msg('VAS_105_RenewalType'), c.renewalName || c.renewalCode) +
                       (c.productName ? detailRow(msg('Product'), c.productName) : '') +
                       descHtml;
            var foot = '<button class="vas_105_acct-btn vas_105_acct-btn--secondary" onclick="(function(){document.getElementById(\'vas_105_overlay_' + widgetID + '\').classList.remove(\'vas_105_acct-overlay--open\');})();">' + esc(msg('VIS_Close')) + '</button>';
            showModal(c.name || c.contractNo || msg('VAS_105_Contract'), msg('VAS_105_ContractDetail'), body, foot, false);
        }

        function openTicketDetail(t) {
            var body = detailRow(msg('VAS_105_TicketNo'), t.ticketNo) +
                       detailRow(msg('Subject'), t.subject) +
                       detailRow(msg('Priority'), t.priorityName || prioLabel(t.priorityCode)) +
                       detailRow(msg('Status'), t.status) +
                       detailRow(msg('VAS_105_Opened'), fmtDate(t.opened)) +
                       detailRow(msg('VAS_105_AgeDays'), t.ageDays != null ? (String(t.ageDays) + ' ' + msg('VIS_Days')) : '') +
                       detailRow(msg('Contact'), t.contact);
            var foot = '<button class="vas_105_acct-btn vas_105_acct-btn--secondary" onclick="(function(){document.getElementById(\'vas_105_overlay_' + widgetID + '\').classList.remove(\'vas_105_acct-overlay--open\');})();">' + esc(msg('VIS_Close')) + '</button>';
            showModal(t.ticketNo || msg('VAS_105_Ticket'), t.subject || '', body, foot, false);
        }

        function openOrderDetail(o) {
            var sym  = getSym(); var prec = getPrec();
            var body = detailRow(msg('VAS_105_OrderNo'), o.orderNo) +
                       detailRow(msg('VAS_105_OrderDate'), fmtDate(o.orderDate)) +
                       detailRow(msg('VAS_105_Items'), o.items) +
                       detailRow(msg('Amount'), fmtFull(toNum(o.amount), sym, prec)) +
                       detailRow(msg('Status'), orderStatusLabel(o.statusCode));
            var foot = '<button class="vas_105_acct-btn vas_105_acct-btn--secondary" onclick="(function(){document.getElementById(\'vas_105_overlay_' + widgetID + '\').classList.remove(\'vas_105_acct-overlay--open\');})();">' + esc(msg('VIS_Close')) + '</button>';
            showModal(o.orderNo || msg('Order'), msg('VAS_OrderDetails'), body, foot, false);
        }

        function openInvoiceDetail(inv) {
            var sym  = getSym(); var prec = getPrec();
            var body = detailRow(msg('InvoiceNo'), inv.invoiceNo) +
                       detailRow(msg('VIS_InvoiceDate'), fmtDate(inv.invoiceDate)) +
                       detailRow(msg('DueDate'), fmtDate(inv.dueDate)) +
                       detailRow(msg('Amount'), fmtFull(toNum(inv.amount), sym, prec)) +
                       detailRow(msg('VAS_105_Paid'), fmtFull(toNum(inv.paid), sym, prec)) +
                       detailRow(msg('VAS_105_PayStatus'), inv.payStatus);
            var foot = '<button class="vas_105_acct-btn vas_105_acct-btn--secondary" onclick="(function(){document.getElementById(\'vas_105_overlay_' + widgetID + '\').classList.remove(\'vas_105_acct-overlay--open\');})();">' + esc(msg('VIS_Close')) + '</button>';
            showModal(inv.invoiceNo || msg('Invoice'), msg('VAS_105_InvoiceDetail'), body, foot, false);
        }

        function openProjectDetail(p) {
            var sym  = getSym(); var prec = getPrec();
            var body = detailRow(msg('Project'), p.name) +
                       detailRow(msg('Status'), projStatusLabel(p.statusCode)) +
                       detailRow(msg('VAS_105_Due'), fmtDate(p.due)) +
                       detailRow(msg('StartDate'), fmtDate(p.startDate)) +
                       detailRow(msg('Lead'), p.lead) +
                       detailRow(msg('Budget'), fmtFull(toNum(p.budget), sym, prec)) +
                       (p.description ? '<div style="margin-top:0.625em;font-size:0.8125em;color:#102C3F;line-height:1.5;">' + esc(p.description) + '</div>' : '');
            var foot = '<button class="vas_105_acct-btn vas_105_acct-btn--secondary" onclick="(function(){document.getElementById(\'vas_105_overlay_' + widgetID + '\').classList.remove(\'vas_105_acct-overlay--open\');})();">' + esc(msg('VIS_Close')) + '</button>';
            showModal(p.name || msg('Project'), msg('VAS_105_ProjectDetail'), body, foot, false);
        }

        // ── Past tickets modal (paged list) ───────────────────────────────────
        function openPastTickets() {
            var body = '<div id="vas_105_past_tick_list_' + widgetID + '">' + skelLines(4) + '</div>';
            var foot = '<button class="vas_105_acct-btn vas_105_acct-btn--secondary" onclick="(function(){document.getElementById(\'vas_105_overlay_' + widgetID + '\').classList.remove(\'vas_105_acct-overlay--open\');})();">' + esc(msg('VIS_Close')) + '</button>';
            showModal(msg('VAS_105_PastTickets'), '', body, foot, true);

            $.ajax({
                url:  VIS.Application.contextUrl + 'VAS/VAS_105_AccountRightPanel/GetTickets',
                type: 'POST',
                data: { bPartnerId: currentBpId, state: 'past', pageOffset: 0, pageSize: 20 },
                async: true,
                success: function (raw) {
                    var parsed = null;
                    try { parsed = (typeof raw === 'string') ? jQuery.parseJSON(raw) : raw; } catch (e) {}
                    var listEl = document.getElementById('vas_105_past_tick_list_' + widgetID);
                    if (!listEl) return;
                    var items = (parsed && parsed.items) ? parsed.items : [];
                    if (!items.length) { listEl.innerHTML = emptyState('VAS_105_NoPastTickets'); return; }
                    var h = '';
                    for (var i = 0; i < items.length; i++) {
                        var t = items[i];
                        h += '<div class="vas_105_acct-tickrow">' +
                             '<span class="vas_105_acct-dot" style="background:#0B6B45;"></span>' +
                             '<span style="font-size:0.8125em;color:var(--acct-muted);width:4.875em;flex-shrink:0;">' + esc(t.ticketNo||'') + '</span>' +
                             '<span style="flex:1;min-width:0;font-size:0.8125em;color:var(--acct-text);overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">' + esc(t.subject||'') + '</span>' +
                             '<span class="vas_105_acct-prio vas_105_acct-prio--resolved">Resolved</span>' +
                             '</div>';
                    }
                    listEl.innerHTML = h;
                },
                error: function () {
                    var listEl = document.getElementById('vas_105_past_tick_list_' + widgetID);
                    if (listEl) listEl.innerHTML = '<div class="vas_105_acct-errtxt">' + esc(msg('VAS_105_LoadError')) + '</div>';
                }
            });
        }

        // ── Open Sales Order window for a specific C_Order_ID ────────────────
        function openOrderInWindow(orderId) {
            if (!orderId || !window.VIS) return;
            $.ajax({
                url:  VIS.Application.contextUrl + 'VAS/VAS_105_AccountRightPanel/GetWindowId',
                type: 'POST',
                data: { windowName: 'VAS_SalesOrder' },
                async: true,
                success: function (raw) {
                    var parsed = null;
                    try { parsed = (typeof raw === 'string') ? jQuery.parseJSON(raw) : raw; } catch (e) {}
                    var windowId = (parsed && parsed.windowId) ? parsed.windowId : 0;
                    if (windowId > 0 && VIS.viewManager && typeof VIS.viewManager.startWindow === 'function') {
                        var q = (VIS.Query && VIS.Query.prototype && typeof VIS.Query.prototype.getEqualQuery === 'function')
                            ? VIS.Query.prototype.getEqualQuery('C_Order_ID', orderId)
                            : null;
                        VIS.viewManager.startWindow(windowId, q);
                    }
                }
            });
        }

        // ── View all Orders modal (paged) ────────────────────────────────────
        function openAllOrders() {
            var sym = getSym(); var prec = getPrec();
            var body = '<div id="vas_105_all_ord_list_' + widgetID + '">' + skelLines(4) + '</div>';
            var foot = '<button class="vas_105_acct-btn vas_105_acct-btn--secondary" onclick="(function(){document.getElementById(\'vas_105_overlay_' + widgetID + '\').classList.remove(\'vas_105_acct-overlay--open\');})();">' + esc(msg('VIS_Close')) + '</button>';
            showModal(msg('Orders'), '', body, foot, true);

            $.ajax({
                url:  VIS.Application.contextUrl + 'VAS/VAS_105_AccountRightPanel/GetOrders',
                type: 'POST',
                data: { bPartnerId: currentBpId, pageOffset: 0, pageSize: 50 },
                async: true,
                success: function (raw) {
                    var parsed = null;
                    try { parsed = (typeof raw === 'string') ? jQuery.parseJSON(raw) : raw; } catch (e) {}
                    var listEl = document.getElementById('vas_105_all_ord_list_' + widgetID);
                    if (!listEl) return;
                    var items = (parsed && parsed.items) ? parsed.items : [];
                    if (!items.length) { listEl.innerHTML = emptyState('VAS_105_NoOrders'); return; }
                    var h = '<div class="vas_105_acct-tablehead" style="grid-template-columns:1fr 1.2fr 1fr 0.8fr;margin-bottom:0.25em;">' +
                            '<span>' + esc(msg('VAS_105_OrderNo'))  + '</span>' +
                            '<span>' + esc(msg('Date'))     + '</span>' +
                            '<span style="text-align:right;">' + esc(msg('Amount')) + '</span>' +
                            '<span style="text-align:right;">' + esc(msg('Status')) + '</span></div>';
                    for (var i = 0; i < items.length; i++) {
                        var o = items[i];
                        h += '<div class="vas_105_acct-clickrow" data-order-id="' + esc(String(o.id || 0)) + '" style="grid-template-columns:1fr 1.2fr 1fr 0.8fr;">' +
                             '<span style="font-size:0.8125em;font-weight:700;color:var(--acct-text);">' + esc(o.orderNo||'') + '</span>' +
                             '<span style="font-size:0.8125em;color:var(--acct-text-2);">' + esc(fmtDate(o.orderDate)) + '</span>' +
                             '<span style="font-size:0.875em;font-weight:700;text-align:right;">' + esc(fmtFull(toNum(o.amount), sym, prec)) + '</span>' +
                             '<span style="text-align:right;"><span class="vas_105_acct-odstatus ' + orderStatusClass(o.statusCode) + '">' + esc(orderStatusLabel(o.statusCode)) + '</span></span>' +
                             '</div>';
                    }
                    listEl.innerHTML = h;

                    // Wire row clicks → navigate to VAS_SalesOrder window
                    var rows = listEl.querySelectorAll('.vas_105_acct-clickrow[data-order-id]');
                    for (var ri = 0; ri < rows.length; ri++) {
                        (function (row) {
                            row.onclick = function (e) {
                                e.stopPropagation();
                                var oid = parseInt(row.getAttribute('data-order-id'), 10) || 0;
                                if (oid > 0) openOrderInWindow(oid);
                            };
                        })(rows[ri]);
                    }
                },
                error: function () {
                    var listEl = document.getElementById('vas_105_all_ord_list_' + widgetID);
                    if (listEl) listEl.innerHTML = '<div class="vas_105_acct-errtxt">' + esc(msg('VAS_105_LoadError')) + '</div>';
                }
            });
        }

        // ── Open AR Invoice window for a specific C_Invoice_ID ───────────────
        function openInvoiceInWindow(invoiceId) {
            if (!invoiceId || !window.VIS) return;
            $.ajax({
                url:  VIS.Application.contextUrl + 'VAS/VAS_105_AccountRightPanel/GetWindowId',
                type: 'POST',
                data: { windowName: 'VAS_ARInvoice' },
                async: true,
                success: function (raw) {
                    var parsed = null;
                    try { parsed = (typeof raw === 'string') ? jQuery.parseJSON(raw) : raw; } catch (e) {}
                    var windowId = (parsed && parsed.windowId) ? parsed.windowId : 0;
                    if (windowId > 0 && VIS.viewManager && typeof VIS.viewManager.startWindow === 'function') {
                        var q = (VIS.Query && VIS.Query.prototype && typeof VIS.Query.prototype.getEqualQuery === 'function')
                            ? VIS.Query.prototype.getEqualQuery('C_Invoice_ID', invoiceId)
                            : null;
                        VIS.viewManager.startWindow(windowId, q);
                    }
                }
            });
        }

        // ── View all Invoices modal (paged) ──────────────────────────────────
        function openAllInvoices() {
            var sym = getSym(); var prec = getPrec();
            var body = '<div id="vas_105_all_inv_list_' + widgetID + '">' + skelLines(4) + '</div>';
            var foot = '<button class="vas_105_acct-btn vas_105_acct-btn--secondary" onclick="(function(){document.getElementById(\'vas_105_overlay_' + widgetID + '\').classList.remove(\'vas_105_acct-overlay--open\');})();">' + esc(msg('VIS_Close')) + '</button>';
            showModal(msg('Invoices'), '', body, foot, true);

            $.ajax({
                url:  VIS.Application.contextUrl + 'VAS/VAS_105_AccountRightPanel/GetInvoices',
                type: 'POST',
                data: { bPartnerId: currentBpId, pageOffset: 0, pageSize: 50 },
                async: true,
                success: function (raw) {
                    var parsed = null;
                    try { parsed = (typeof raw === 'string') ? jQuery.parseJSON(raw) : raw; } catch (e) {}
                    var listEl = document.getElementById('vas_105_all_inv_list_' + widgetID);
                    if (!listEl) return;
                    var items = (parsed && parsed.items) ? parsed.items : [];
                    if (!items.length) { listEl.innerHTML = emptyState('VAS_105_NoInvoices'); return; }
                    var h = '<div class="vas_105_acct-tablehead" style="grid-template-columns:1fr 0.9fr 1fr 0.8fr;margin-bottom:0.25em;">' +
                            '<span>' + esc(msg('InvoiceNo')) + '</span>' +
                            '<span style="text-align:right;">' + esc(msg('Amount'))  + '</span>' +
                            '<span style="text-align:right;">' + esc(msg('DueDate')) + '</span>' +
                            '<span style="text-align:right;">' + esc(msg('Status'))  + '</span></div>';
                    for (var i = 0; i < items.length; i++) {
                        var inv = items[i];
                        h += '<div class="vas_105_acct-clickrow" data-invoice-id="' + esc(String(inv.id || 0)) + '" style="grid-template-columns:1fr 0.9fr 1fr 0.8fr;">' +
                             '<span style="font-size:0.8125em;font-weight:700;color:var(--acct-text);">' + esc(inv.invoiceNo||'') + '</span>' +
                             '<span style="font-size:0.875em;font-weight:700;text-align:right;">' + esc(fmtFull(toNum(inv.amount), sym, prec)) + '</span>' +
                             '<span style="font-size:0.8125em;color:var(--acct-text-2);text-align:right;">' + esc(fmtDate(inv.dueDate)) + '</span>' +
                             '<span style="text-align:right;"><span class="vas_105_acct-ivstatus ' + ivStatusClass(inv.payStatus) + '">' + esc(inv.payStatus||'Open') + '</span></span>' +
                             '</div>';
                    }
                    listEl.innerHTML = h;

                    // Wire row clicks → navigate to VAS_ARInvoice window
                    var rows = listEl.querySelectorAll('.vas_105_acct-clickrow[data-invoice-id]');
                    for (var ri = 0; ri < rows.length; ri++) {
                        (function (row) {
                            row.onclick = function (e) {
                                e.stopPropagation();
                                var iid = parseInt(row.getAttribute('data-invoice-id'), 10) || 0;
                                if (iid > 0) openInvoiceInWindow(iid);
                            };
                        })(rows[ri]);
                    }
                },
                error: function () {
                    var listEl = document.getElementById('vas_105_all_inv_list_' + widgetID);
                    if (listEl) listEl.innerHTML = '<div class="vas_105_acct-errtxt">' + esc(msg('VAS_105_LoadError')) + '</div>';
                }
            });
        }

        // ── Log Activity modal ────────────────────────────────────────────────
        function openLogActivity() {
            var body =
                '<div class="vas_105_acct-ftypes" id="vas_105_ftypes_' + widgetID + '">' +
                  ['Call','Note','Visit'].map(function(t,i) {
                      var icons = ['ti-phone','ti-note','ti-map-pin'];
                      return '<button class="vas_105_acct-ftype' + (i===0?' vas_105_acct-ftype--sel':'') + '" data-atype="' + t + '" type="button">' +
                             '<i class="ti ' + icons[i] + '" style="font-size:1.25em;"></i>' + esc(t) + '</button>';
                  }).join('') +
                '</div>' +
                '<div class="vas_105_acct-field"><label>' + esc(msg('Subject')) + '</label>' +
                '<input type="text" id="vas_105_act_subj_' + widgetID + '" /></div>' +
                '<div class="vas_105_acct-fieldrow">' +
                  '<div class="vas_105_acct-field"><label>' + esc(msg('Date')) + '</label>' +
                  '<input type="date" id="vas_105_act_date_' + widgetID + '" /></div>' +
                  '<div class="vas_105_acct-field"><label>' + esc(msg('VAS_105_Duration')) + '</label>' +
                  '<input type="text" id="vas_105_act_dur_' + widgetID + '" placeholder="e.g. 30 min" /></div>' +
                '</div>';

            var foot =
                '<button class="vas_105_acct-btn vas_105_acct-btn--secondary" onclick="(function(){document.getElementById(\'vas_105_overlay_' + widgetID + '\').classList.remove(\'vas_105_acct-overlay--open\');})();">' + esc(msg('VAS_Cancel')) + '</button>' +
                '<button class="vas_105_acct-btn vas_105_acct-btn--primary" id="vas_105_act_submit_' + widgetID + '">' + esc(msg('VIS_Submit')) + '</button>';

            showModal(msg('VAS_105_LogActivity'), '', body, foot, false);

            // Wire type toggle
            var ftypes = document.getElementById('vas_105_ftypes_' + widgetID);
            if (ftypes) {
                $(ftypes).find('.vas_105_acct-ftype').on('click', function () {
                    $(ftypes).find('.vas_105_acct-ftype').removeClass('vas_105_acct-ftype--sel');
                    $(this).addClass('vas_105_acct-ftype--sel');
                });
            }

            // Wire submit
            var btnSubmit = document.getElementById('vas_105_act_submit_' + widgetID);
            if (btnSubmit) {
                btnSubmit.onclick = function () {
                    var actType = '';
                    var ftypesSel = document.querySelectorAll('#vas_105_ftypes_' + widgetID + ' .vas_105_acct-ftype--sel');
                    if (ftypesSel.length) actType = ftypesSel[0].getAttribute('data-atype') || 'Call';
                    var subj = $.trim((document.getElementById('vas_105_act_subj_' + widgetID) || {}).value || '');
                    var date = $.trim((document.getElementById('vas_105_act_date_' + widgetID) || {}).value || '');
                    var dur  = $.trim((document.getElementById('vas_105_act_dur_'  + widgetID) || {}).value || '');
                    submitActivity(actType, subj, date, dur);
                };
            }
        }

        // ── Schedule Meeting — standard platform Appointments form ──────────
        function openSchedule() {
            if (!window.VIS || !VIS.AppointmentsForm || typeof VIS.AppointmentsForm.init !== 'function') return;
            var tableId  = $self.table_ID || 0;
            var userId   = (VIS.context && typeof VIS.context.getAD_User_ID  === 'function') ? VIS.context.getAD_User_ID()  : 0;
            var userName = (VIS.context && typeof VIS.context.getAD_UserName === 'function') ? VIS.context.getAD_UserName() : '';
            VIS.AppointmentsForm.init(tableId, currentBpId, userId, userName, false);
            $(document).one('ajaxComplete', function () {
                fetchSection('timeline', 'GetEngagement', {}, null);
            });
        }

        // ── (kept for reference — no longer called) ──────────────────────────
        function openSchedule_legacy_unused() {
            var now = new Date();
            now.setMinutes(now.getMinutes() < 30 ? 30 : 60, 0, 0);
            var endDt = new Date(now.getTime() + 30 * 60000);
            function pad(n) { return n < 10 ? '0' + n : '' + n; }
            function dtStr(d) {
                return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()) +
                       'T' + pad(d.getHours()) + ':' + pad(d.getMinutes());
            }
            var startVal = dtStr(now);
            var endVal   = dtStr(endDt);

            // Inline SVGs — Tabler Icons font is not available in this context
            var ICO_WORLD    = '<svg width="1.1em" height="1.1em" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><circle cx="12" cy="12" r="10"/><line x1="2" y1="12" x2="22" y2="12"/><path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z"/></svg>';
            var ICO_PENCIL   = '<svg width="1.1em" height="1.1em" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path d="M17 3a2.828 2.828 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5L17 3z"/></svg>';
            var ICO_PIN      = '<svg width="1.1em" height="1.1em" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0z"/><circle cx="12" cy="10" r="3"/></svg>';
            var ICO_GRID     = '<svg width="1.1em" height="1.1em" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><rect x="3" y="3" width="7" height="7"/><rect x="14" y="3" width="7" height="7"/><rect x="14" y="14" width="7" height="7"/><rect x="3" y="14" width="7" height="7"/></svg>';
            var ICO_CALENDAR = '<svg width="1.1em" height="1.1em" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><rect width="18" height="18" x="3" y="4" rx="2" ry="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/></svg>';
            var ICO_REFRESH  = '<svg width="1.1em" height="1.1em" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path d="M23 4v6h-6"/><path d="M1 20v-6h6"/><path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"/></svg>';
            var ICO_BOOK     = '<svg width="1.1em" height="1.1em" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path d="M2 3h6a4 4 0 0 1 4 4v14a3 3 0 0 0-3-3H2z"/><path d="M22 3h-6a4 4 0 0 0-4 4v14a3 3 0 0 1 3-3h7z"/></svg>';
            var ICO_NOTES    = '<svg width="1.1em" height="1.1em" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/><polyline points="10 9 9 9 8 9"/></svg>';

            var body =
                '<div class="vas_105_apt-form">' +
                  // Meeting URL
                  '<div class="vas_105_apt-row">' +
                    '<span class="vas_105_apt-icon">' + ICO_WORLD + '</span>' +
                    '<input type="text" class="vas_105_apt-input" id="vas_105_apt_url_' + widgetID + '"' +
                    ' placeholder="' + esc(msg('VAS_105_MeetingURL')) + '" />' +
                  '</div>' +
                  // Title (required)
                  '<div class="vas_105_apt-row">' +
                    '<span class="vas_105_apt-icon">' + ICO_PENCIL + '</span>' +
                    '<input type="text" class="vas_105_apt-input" id="vas_105_apt_title_' + widgetID + '"' +
                    ' placeholder="' + esc(msg('Title')) + '" />' +
                    '<span class="vas_105_apt-reqmark" title="Required">&#9679;</span>' +
                  '</div>' +
                  // Location
                  '<div class="vas_105_apt-row">' +
                    '<span class="vas_105_apt-icon">' + ICO_PIN + '</span>' +
                    '<input type="text" class="vas_105_apt-input" id="vas_105_apt_loc_' + widgetID + '"' +
                    ' placeholder="' + esc(msg('VAS_105_Location')) + '" />' +
                  '</div>' +
                  // Category + Is All Day + Private
                  '<div class="vas_105_apt-row">' +
                    '<span class="vas_105_apt-icon">' + ICO_GRID + '</span>' +
                    '<div class="vas_105_apt-catrow">' +
                      '<select class="vas_105_apt-select" id="vas_105_apt_cat_' + widgetID + '">' +
                        '<option value="">'  + esc(msg('None'))        + '</option>' +
                        '<option value="B">' + esc(msg('Business'))    + '</option>' +
                        '<option value="L">' + esc(msg('Letter'))      + '</option>' +
                        '<option value="C">' + esc(msg('Call'))        + '</option>' +
                        '<option value="T">' + esc(msg('Task'))        + '</option>' +
                        '<option value="E">' + esc(msg('Email'))       + '</option>' +
                        '<option value="A">' + esc(msg('Appointment')) + '</option>' +
                        '<option value="P">' + esc(msg('Competition')) + '</option>' +
                      '</select>' +
                      '<div class="vas_105_apt-chkgroup">' +
                        '<label class="vas_105_apt-chklabel">' +
                          '<input type="checkbox" id="vas_105_apt_allday_' + widgetID + '" /> ' +
                          esc(msg('IsAllDay')) +
                        '</label>' +
                        '<label class="vas_105_apt-chklabel">' +
                          '<input type="checkbox" id="vas_105_apt_priv_' + widgetID + '" /> ' +
                          esc(msg('IsPrivate')) +
                        '</label>' +
                      '</div>' +
                    '</div>' +
                  '</div>' +
                  // Start + End datetime
                  '<div class="vas_105_apt-row">' +
                    '<span class="vas_105_apt-icon">' + ICO_CALENDAR + '</span>' +
                    '<div class="vas_105_apt-dtrow">' +
                      '<input type="datetime-local" class="vas_105_apt-dtfield" id="vas_105_apt_start_' + widgetID + '" value="' + startVal + '" />' +
                      '<input type="datetime-local" class="vas_105_apt-dtfield" id="vas_105_apt_end_'   + widgetID + '" value="' + endVal   + '" />' +
                    '</div>' +
                  '</div>' +
                  // Recurrence
                  '<div class="vas_105_apt-row">' +
                    '<span class="vas_105_apt-icon">' + ICO_REFRESH + '</span>' +
                    '<select class="vas_105_apt-select" id="vas_105_apt_recur_' + widgetID + '">' +
                      '<option value="N">' + esc(msg('Never'))   + '</option>' +
                      '<option value="D">' + esc(msg('Daily'))   + '</option>' +
                      '<option value="W">' + esc(msg('Weekly'))  + '</option>' +
                      '<option value="M">' + esc(msg('Monthly')) + '</option>' +
                      '<option value="Y">' + esc(msg('Yearly'))  + '</option>' +
                    '</select>' +
                  '</div>' +
                  // Contacts — email tag input
                  '<div class="vas_105_apt-row">' +
                    '<span class="vas_105_apt-icon">' + ICO_BOOK + '</span>' +
                    '<div class="vas_105_apt-tagbox" id="vas_105_apt_contacts_box_' + widgetID + '">' +
                      '<input type="text" class="vas_105_apt-taginput" id="vas_105_apt_contacts_' + widgetID + '" autocomplete="off" />' +
                      '<div class="vas_105_apt-tagdrop" id="vas_105_apt_contacts_drop_' + widgetID + '"></div>' +
                    '</div>' +
                  '</div>' +
                  // Description
                  '<div class="vas_105_apt-row vas_105_apt-row--top">' +
                    '<span class="vas_105_apt-icon">' + ICO_NOTES + '</span>' +
                    '<textarea class="vas_105_apt-textarea" id="vas_105_apt_desc_' + widgetID + '" rows="4"></textarea>' +
                  '</div>' +
                '</div>';

            var foot =
                '<button class="vas_105_acct-btn vas_105_acct-btn--primary" id="vas_105_apt_save_' + widgetID + '">' + esc(msg('Save')) + '</button>' +
                '<button class="vas_105_acct-btn vas_105_acct-btn--secondary" id="vas_105_apt_cancel_' + widgetID + '">' + esc(msg('VAS_Cancel')) + '</button>';

            showModal(msg('VAS_105_ScheduleMeeting'), '', body, foot, true);

            var btnCancel = document.getElementById('vas_105_apt_cancel_' + widgetID);
            if (btnCancel) btnCancel.onclick = function (e) { if (e) { e.stopPropagation(); e.stopImmediatePropagation(); } closeModal(); };

            // ── Email tag-chip input ───────────────────────────────────────────
            var aptContacts   = [];
            var contactsInput = document.getElementById('vas_105_apt_contacts_' + widgetID);
            var contactsDrop  = document.getElementById('vas_105_apt_contacts_drop_' + widgetID);
            var contactsBox   = document.getElementById('vas_105_apt_contacts_box_' + widgetID);

            function isValidEmail(v) { return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v); }

            function renderChips() {
                var existing = contactsBox.querySelectorAll('.vas_105_apt-chip');
                for (var ci = 0; ci < existing.length; ci++) existing[ci].parentNode.removeChild(existing[ci]);
                for (var ei = 0; ei < aptContacts.length; ei++) {
                    (function (email, idx) {
                        var chip = document.createElement('span');
                        chip.className = 'vas_105_apt-chip';
                        chip.innerHTML = esc(email) + '<button type="button" class="vas_105_apt-chip__remove" aria-label="Remove">&#x2715;</button>';
                        chip.querySelector('.vas_105_apt-chip__remove').onclick = function (e) {
                            if (e) { e.stopPropagation(); e.stopImmediatePropagation(); }
                            aptContacts.splice(idx, 1);
                            renderChips();
                        };
                        contactsBox.insertBefore(chip, contactsInput);
                    })(aptContacts[ei], ei);
                }
            }

            function hideDrop() {
                if (contactsDrop) { contactsDrop.style.display = 'none'; contactsDrop.innerHTML = ''; }
            }

            function showDrop(email) {
                if (!contactsDrop) return;
                contactsDrop.innerHTML = '';
                var item = document.createElement('div');
                item.className = 'vas_105_apt-tagdrop-item';
                item.textContent = msg('AddNew') + ': ' + email;
                item.onmousedown = function (e) {
                    if (e) { e.preventDefault(); e.stopPropagation(); }
                    aptContacts.push(email);
                    contactsInput.value = '';
                    renderChips();
                    hideDrop();
                };
                contactsDrop.appendChild(item);
                contactsDrop.style.display = 'block';
            }

            if (contactsInput) {
                contactsInput.oninput = function () {
                    var v = $.trim(contactsInput.value);
                    if (v && isValidEmail(v)) showDrop(v); else hideDrop();
                };
                contactsInput.onblur = function () { setTimeout(hideDrop, 150); };
                contactsInput.onkeydown = function (e) {
                    if (e.key === 'Enter' || e.keyCode === 13) {
                        e.preventDefault();
                        var v = $.trim(contactsInput.value);
                        if (v && isValidEmail(v)) {
                            aptContacts.push(v);
                            contactsInput.value = '';
                            renderChips();
                            hideDrop();
                        }
                    }
                };
            }

            var btnSave = document.getElementById('vas_105_apt_save_' + widgetID);
            if (btnSave) {
                btnSave.onclick = function (e) {
                    if (e) { e.stopPropagation(); e.stopImmediatePropagation(); }
                    var title    = $.trim((document.getElementById('vas_105_apt_title_' + widgetID) || {}).value || '');
                    var startDT  = $.trim((document.getElementById('vas_105_apt_start_' + widgetID) || {}).value || '');
                    var endDT    = $.trim((document.getElementById('vas_105_apt_end_'   + widgetID) || {}).value || '');
                    var location = $.trim((document.getElementById('vas_105_apt_loc_'   + widgetID) || {}).value || '');
                    var desc     = $.trim((document.getElementById('vas_105_apt_desc_'  + widgetID) || {}).value || '');
                    if (!title) {
                        var el = document.getElementById('vas_105_apt_title_' + widgetID);
                        if (el) el.focus();
                        return;
                    }
                    submitMeeting(title, startDT, endDT, location, desc);
                };
            }
        }

        // ── Edit ──────────────────────────────────────────────────────────────
        function openEdit() {
            if (!currentBpId || currentBpId <= 0 || !window.VIS) return;
            var windowId = 0;
            if (VIS.ZoomTarget && typeof VIS.ZoomTarget.getZoomAD_Window_ID === 'function') {
                try { windowId = VIS.ZoomTarget.getZoomAD_Window_ID('C_BPartner', 0, null, false) || 0; } catch (e) { windowId = 0; }
            }
            if (windowId > 0 && VIS.viewManager && typeof VIS.viewManager.startWindow === 'function') {
                var zoomQuery = VIS.Query.prototype.getEqualQuery('C_BPartner_ID', currentBpId);
                VIS.viewManager.startWindow(windowId, zoomQuery);
            }
        }

        // ── Engagement detail modals ──────────────────────────────────────────

        function openNoteDetailModal(noteId) {
            var SVG_NOTE = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14.5 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7.5L14.5 2z"/><polyline points="14 2 14 8 20 8"/></svg>';
            showModal(msg('VAS_105_TouchPoint'), '', '<div class="vas_105_acct-empty">' + esc(msg('VAS_105_Loading')) + '</div>', '', false);
            fetchModal('GetNoteDetail', { noteId: noteId }, function (err, data) {
                var bodyEl = document.getElementById('vas_105_mbody_' + widgetID);
                var footEl = document.getElementById('vas_105_mfoot_' + widgetID);
                if (err || !data || !data.id) {
                    if (bodyEl) bodyEl.innerHTML = '<div class="vas_105_acct-empty">' + esc(msg('VIS_NoData')) + '</div>';
                    if (footEl) footEl.innerHTML = '<button class="vas_105_acct-btn vas_105_acct-btn--secondary" id="' + widgetID + '_ndClose">' + esc(msg('Close')) + '</button>';
                    $root.find('#' + widgetID + '_ndClose').on('click', function (e) { e.stopPropagation(); e.stopImmediatePropagation(); closeModal(); });
                    return;
                }
                document.getElementById('vas_105_mmeta_' + widgetID).textContent = data.whenTs ? fmtEngTs(data.whenTs) : '';
                var bodyHtml = [
                    '<div style="margin-bottom:0.75em;">',
                    '  <span class="vas_105_eng-badge" style="background:#FEF3C7;color:#92400E;">',
                    '    <span style="display:inline-flex;align-items:center;gap:0.2em;vertical-align:middle;">', SVG_NOTE, ' ', esc(msg('Note')), '</span>',
                    '  </span>',
                    '</div>',
                    data.title ? '<div style="font-size:1em;font-weight:700;color:var(--acct-text);margin-bottom:0.5em;">' + esc(data.title) + '</div>' : '',
                    '<table style="width:100%;border-collapse:collapse;font-size:0.875em;margin-bottom:0.75em;">',
                    '  <tr>',
                    '    <td style="color:var(--acct-muted);padding:0.25em 0.5em 0.25em 0;">' + esc(msg('VAS_105_When')) + '</td>',
                    '    <td style="color:var(--acct-text);">' + esc(fmtEngTs(data.whenTs || '')) + '</td>',
                    data.who ? '    <td style="color:var(--acct-muted);padding:0.25em 0.5em 0.25em 1em;">' + esc(msg('Details')) + '</td><td style="color:var(--acct-text);">' + esc(data.who) + '</td>' : '<td></td><td></td>',
                    '  </tr>',
                    '</table>',
                    data.body ? '<div style="font-size:0.875em;color:var(--acct-text);background:#F6FAFE;border:0.0625em solid var(--acct-border);border-radius:0.5em;padding:0.75em;white-space:pre-wrap;word-break:break-word;">' + esc(data.body) + '</div>' : ''
                ].join('');
                if (bodyEl) bodyEl.innerHTML = bodyHtml;
                if (footEl) footEl.innerHTML = '<button class="vas_105_acct-btn vas_105_acct-btn--secondary" id="' + widgetID + '_ndClose">' + esc(msg('Close')) + '</button>';
                $root.find('#' + widgetID + '_ndClose').on('click', function (e) { e.stopPropagation(); e.stopImmediatePropagation(); closeModal(); });
            });
        }

        function openEmailDetailModal(emailId) {
            var SVG_MAIL  = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="2" y="4" width="20" height="16" rx="2"/><path d="m22 7-8.97 5.7a1.94 1.94 0 0 1-2.06 0L2 7"/></svg>';
            var SVG_REPLY = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 17 4 12 9 7"/><path d="M20 18v-2a4 4 0 0 0-4-4H4"/></svg>';
            showModal(msg('VAS_105_TouchPoint'), '', '<div class="vas_105_acct-empty">' + esc(msg('VAS_105_Loading')) + '</div>', '', false);
            fetchModal('GetEmailDetail', { emailId: emailId }, function (err, data) {
                var bodyEl = document.getElementById('vas_105_mbody_' + widgetID);
                var footEl = document.getElementById('vas_105_mfoot_' + widgetID);
                if (err || !data || !data.id) {
                    if (bodyEl) bodyEl.innerHTML = '<div class="vas_105_acct-empty">' + esc(msg('VIS_NoData')) + '</div>';
                    if (footEl) footEl.innerHTML = '<button class="vas_105_acct-btn vas_105_acct-btn--secondary" id="' + widgetID + '_emdClose">' + esc(msg('Close')) + '</button>';
                    $root.find('#' + widgetID + '_emdClose').on('click', function (e) { e.stopPropagation(); e.stopImmediatePropagation(); closeModal(); });
                    return;
                }

                document.getElementById('vas_105_mmeta_' + widgetID).textContent = data.whenTs ? fmtEngTs(data.whenTs) : '';

                var dirLabel   = data.direction === 'in' ? msg('VAS_105_Incoming') : msg('VAS_105_Outgoing');
                var detailAddr = data.direction === 'in' ? (data.fromEmail || '') : (data.toEmail || '');

                var tmpDiv = document.createElement('div');
                tmpDiv.innerHTML = data.body || '';
                var plainBody = (tmpDiv.textContent || tmpDiv.innerText || '').replace(/\s+/g, ' ').trim();
                var quoteText = plainBody ? plainBody.substring(0, 200) + (plainBody.length > 200 ? '…' : '') : '';

                var bodyHtml = [
                    '<div class="vas_105_emd-top">',
                    '  <span class="vas_105_eng-badge" style="background:#DBEAFE;color:#1D4ED8;">',
                    '    <span style="display:inline-flex;align-items:center;gap:0.2em;vertical-align:middle;">', SVG_MAIL, ' ', esc(msg('EMail')), '</span>',
                    '  </span>',
                    '  <span class="vas_105_emd-heading">', esc(data.subject || msg('EMail')), '</span>',
                    '</div>',
                    quoteText ? '<div class="vas_105_emd-quote">' + esc(quoteText) + '</div>' : '',
                    '<table class="vas_105_emd-meta">',
                    '  <tr>',
                    '    <td class="vas_105_emd-meta-label">', esc(msg('Type')), '</td>',
                    '    <td class="vas_105_emd-meta-value">', esc(msg('EMail')), '</td>',
                    '    <td class="vas_105_emd-meta-label">', esc(msg('VAS_105_Direction')), '</td>',
                    '    <td class="vas_105_emd-meta-value">', esc(dirLabel), '</td>',
                    '  </tr>',
                    '  <tr>',
                    '    <td class="vas_105_emd-meta-label">', esc(msg('VAS_105_When')), '</td>',
                    '    <td class="vas_105_emd-meta-value">', esc(fmtEngTs(data.whenTs || '')), '</td>',
                    '    <td class="vas_105_emd-meta-label">', esc(msg('Details')), '</td>',
                    '    <td class="vas_105_emd-meta-value">', esc(detailAddr), '</td>',
                    '  </tr>',
                    data.who ? [
                        '  <tr>',
                        '    <td class="vas_105_emd-meta-label">', esc(msg('VAS_105_People')), '</td>',
                        '    <td class="vas_105_emd-meta-value" colspan="3">', esc(data.who), '</td>',
                        '  </tr>'
                    ].join('') : '',
                    '</table>',
                    data.body ? '<div class="vas_105_emd-body">' + data.body + '</div>' : ''
                ].join('');

                var footHtml = [
                    '<button class="vas_105_acct-btn vas_105_acct-btn--secondary" id="', widgetID, '_emdClose">', esc(msg('Close')), '</button>',
                    '<button class="vas_105_acct-btn vas_105_acct-btn--primary" id="', widgetID, '_emdReply">', SVG_REPLY, ' ', esc(msg('VAS_105_Reply')), '</button>'
                ].join('');

                if (bodyEl) bodyEl.innerHTML = bodyHtml;
                if (footEl) footEl.innerHTML = footHtml;

                $root.find('#' + widgetID + '_emdClose').on('click', function (e) {
                    e.stopPropagation(); e.stopImmediatePropagation();
                    closeModal();
                });

                $root.find('#' + widgetID + '_emdReply').on('click', function (e) {
                    e.stopPropagation(); e.stopImmediatePropagation();
                    if (!window.VIS || typeof VIS.Email !== 'function' || typeof VIS.CFrame !== 'function') return;
                    closeModal();
                    var tableId  = $self.table_ID || 0;
                    var replyTo  = data.direction === 'in' ? (data.fromEmail || '') : (data.toEmail || '');
                    var subject  = 'RE: ' + (data.subject || '');
                    var replyBody = '<br><br><hr>' + (data.body || '');
                    var email    = new VIS.Email(replyTo, null, null, currentBpId, true, true, tableId, replyBody, subject, null);
                    var c        = new VIS.CFrame();
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
            var SVG_CHECK    = '<svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>';
            var SVG_SPINNER  = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" class="vas_105_spin"><circle cx="12" cy="12" r="10" opacity="0.25"/><path d="M12 2a10 10 0 0 1 10 10"/></svg>';

            showModal(msg('VAS_105_Meeting'), '', '<div class="vas_105_acct-empty">' + esc(msg('VAS_105_Loading')) + '</div>', '', true);

            fetchModal('GetMeetingDetail', { meetingId: meetingId }, function (err, data) {
                var bodyEl = document.getElementById('vas_105_mbody_' + widgetID);
                var footEl = document.getElementById('vas_105_mfoot_' + widgetID);
                if (err || !data || !data.id) {
                    if (bodyEl) bodyEl.innerHTML = '<div class="vas_105_acct-empty">' + esc(msg('VIS_NoData')) + '</div>';
                    if (footEl) footEl.innerHTML = '<button class="vas_105_acct-btn vas_105_acct-btn--secondary" id="' + widgetID + '_mtgClose">' + esc(msg('Close')) + '</button>';
                    $root.find('#' + widgetID + '_mtgClose').on('click', function (e) { e.stopPropagation(); e.stopImmediatePropagation(); closeModal(); });
                    return;
                }

                // Header meta: "Account · Apr 14, 10:00am"
                var acctName = (sectionState.overview.data && sectionState.overview.data.name) ? sectionState.overview.data.name : '';
                var metaStr  = [acctName, data.startDate ? fmtEngTs(data.startDate) : ''].filter(Boolean).join(' · ');
                document.getElementById('vas_105_mmeta_' + widgetID).textContent = metaStr;

                // Duration string: "48m" or "1h 30m"
                var durStr = '';
                if (data.durationMins > 0) {
                    var dh = Math.floor(data.durationMins / 60), dm = data.durationMins % 60;
                    durStr = dh > 0 ? (dh + 'h' + (dm > 0 ? ' ' + dm + 'm' : '')) : (dm + 'm');
                }

                // Attendee list and count
                var attendeeList  = data.attendees ? data.attendees.split(',').filter(function(s){ return $.trim(s); }) : [];
                var attendeeCount = attendeeList.length;
                // Display first name only (first word) for each attendee
                var displayNames  = attendeeList.map(function(n){ return $.trim(n).split(' ')[0]; }).join(', ');

                // Details: "Google Meet · 48m · 4 attended"
                var detailParts = [];
                if (data.location)    detailParts.push(esc(data.location));
                if (durStr)           detailParts.push(esc(durStr));
                if (attendeeCount)    detailParts.push(attendeeCount + ' ' + esc(msg('VAS_105_Attended')));
                var detailsHtml = detailParts.join(' &middot; ') || '&mdash;';

                // Transcript block
                var transcriptHtml = '';
                if (data.transcript) {
                    var lines = String(data.transcript).replace(/\r\n/g, '\n').split('\n');
                    var tLines = '';
                    for (var i = 0; i < lines.length; i++) {
                        var line = $.trim(lines[i]);
                        if (!line) continue;
                        var ci = line.indexOf(':');
                        if (ci > 0 && ci < 50) {
                            tLines += '<div class="vas_105_mtg-tline"><span class="vas_105_mtg-speaker">' + esc(line.substring(0, ci)) + ':</span> ' + esc($.trim(line.substring(ci + 1))) + '</div>';
                        } else {
                            tLines += '<div class="vas_105_mtg-tline">' + esc(line) + '</div>';
                        }
                    }
                    transcriptHtml = [
                        '<hr class="vas_105_mtg-divider">',
                        '<div class="vas_105_mtg-trans-head">',
                        '  <span class="vas_105_mtg-trans-label">', SVG_TRS, ' <strong>', esc(msg('Transcript')), '</strong> &middot; ', esc(msg('Downloaded')), '</span>',
                        '  <button class="vas_105_acct-btn vas_105_acct-btn--secondary vas_105_acct-btn--sm" id="', widgetID, '_btnSaveTxt">', SVG_DOWNLOAD, ' ', esc(msg('VAS_105_SaveTxt')), '</button>',
                        '</div>',
                        '<div class="vas_105_mtg-trans-box">', tLines, '</div>'
                    ].join('');
                }

                var bodyHtml = [
                    // ── Hero: badge + subject ──
                    '<div class="vas_105_mtg-hero">',
                    '  <span class="vas_105_mtg-badge">', esc(msg('VAS_105_Meeting')), '</span>',
                    '  <span class="vas_105_mtg-title">', esc(data.subject || '&mdash;'), '</span>',
                    '</div>',

                    // ── Info grid: Type/When row + Details/People row ──
                    '<div class="vas_105_mtg-infogrid">',
                    '  <div class="vas_105_mtg-infogrid__row">',
                    '    <div class="vas_105_mtg-infogrid__cell">',
                    '      <span class="vas_105_mtg-info-lbl">', esc(msg('Type')), '</span>',
                    '      <span class="vas_105_mtg-info-val">', esc(msg('VAS_105_Meeting')), '</span>',
                    '    </div>',
                    '    <div class="vas_105_mtg-infogrid__cell">',
                    '      <span class="vas_105_mtg-info-lbl">', esc(msg('When')), '</span>',
                    '      <span class="vas_105_mtg-info-val">', data.startDate ? esc(fmtEngTs(data.startDate)) : '&mdash;', '</span>',
                    '    </div>',
                    '  </div>',
                    '  <div class="vas_105_mtg-infogrid__row">',
                    '    <div class="vas_105_mtg-infogrid__cell">',
                    '      <span class="vas_105_mtg-info-lbl">', esc(msg('Details')), '</span>',
                    '      <span class="vas_105_mtg-info-val">', detailsHtml, '</span>',
                    '    </div>',
                    '    <div class="vas_105_mtg-infogrid__cell">',
                    '      <span class="vas_105_mtg-info-lbl">', esc(msg('People')), '</span>',
                    '      <span class="vas_105_mtg-info-val">', displayNames ? esc(displayNames) : '&mdash;', '</span>',
                    '    </div>',
                    '  </div>',
                    '</div>',

                    // ── Comments: plain-text display block ──
                    data.comments
                        ? '<div class="vas_105_mtg-notes">' + esc(data.comments) + '</div>'
                        : '',

                    // ── Meeting URL (editable) ──
                    '<div class="vas_105_mtg-field">',
                    '  <label class="vas_105_mtg-field-label">', esc(msg('VAS_105_MeetingUrl')), '</label>',
                    '  <input type="text" class="vas_105_mtg-input" id="', widgetID, '_mtgUrl" value="', esc(data.meetingUrl || ''), '">',
                    '</div>',

                    // ── Hidden textarea carries comments for Save ──
                    '<textarea id="', widgetID, '_mtgComments" style="display:none;">', esc(data.comments || ''), '</textarea>',

                    transcriptHtml
                ].join('');

                var footHtml = [
                    '<button class="vas_105_acct-btn vas_105_acct-btn--secondary" id="', widgetID, '_mtgClose">', esc(msg('Close')), '</button>',
                    '<button class="vas_105_acct-btn vas_105_acct-btn--primary"   id="', widgetID, '_mtgSave">', SVG_CHECK, ' ', esc(msg('Save')), '</button>'
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
                    e.stopPropagation(); e.stopImmediatePropagation();
                    closeModal();
                });

                $root.find('#' + widgetID + '_mtgSave').on('click', function (e) {
                    e.stopPropagation(); e.stopImmediatePropagation();
                    var $btn      = $(this).prop('disabled', true);
                    var $closeBtn = $root.find('#' + widgetID + '_mtgClose').prop('disabled', true);
                    var origHtml  = $btn.html();
                    $btn.html(SVG_SPINNER + ' ' + esc(msg('Save')));
                    var urlVal      = $.trim($root.find('#' + widgetID + '_mtgUrl').val());
                    var commentsVal = $root.find('#' + widgetID + '_mtgComments').val();
                    $.ajax({
                        url:  VIS.Application.contextUrl + 'VAS/VAS_105_AccountRightPanel/SaveMeetingComments',
                        type: 'POST',
                        data: { meetingId: meetingId, comments: commentsVal, meetingUrl: urlVal },
                        success: function (raw) {
                            var res = null;
                            try { res = JSON.parse(typeof raw === 'string' ? JSON.parse(raw) : raw); } catch (ex) { try { res = (typeof raw === 'string') ? JSON.parse(raw) : raw; } catch (ex2) {} }
                            $btn.prop('disabled', false).html(origHtml);
                            $closeBtn.prop('disabled', false);
                            if (res && res.success) {
                                closeModal();
                                fetchSection('timeline', 'GetEngagement', {}, null);
                            } else {
                                var errMsg = (res && res.error) ? res.error : msg('Error');
                                if (window.VIS && VIS.ADialog) VIS.ADialog.error(errMsg, true, '', '');
                            }
                        },
                        error: function () {
                            $btn.prop('disabled', false).html(origHtml);
                            $closeBtn.prop('disabled', false);
                        }
                    });
                });
            });
        }

        // ── Engagement email helpers ──────────────────────────────────────────
        function openSendEmailModal() {
            if (!window.VIS || typeof VIS.Email !== 'function' || typeof VIS.CFrame !== 'function') return;
            var tableId = $self.table_ID || 0;
            var email   = new VIS.Email('', null, null, currentBpId, true, true, tableId, null, '', null);
            var c       = new VIS.CFrame();
            c.setName(msg('EMail'));
            c.setTitle(msg('EMail'));
            c.hideHeader(true);
            c.setContent(email);
            c.show();
            email.initializeComponent();
        }

        // Opens the standard VIS Email compose window pre-addressed to a contact
        function openContactEmailCompose(toAddr) {
            if (!window.VIS || typeof VIS.Email !== 'function' || typeof VIS.CFrame !== 'function') return;
            var tableId = $self.table_ID || 0;
            var email   = new VIS.Email(toAddr || '', null, null, currentBpId, true, true, tableId, null, '', null);
            var c       = new VIS.CFrame();
            c.setName(msg('EMail'));
            c.setTitle(msg('EMail'));
            c.hideHeader(true);
            c.setContent(email);
            c.show();
            email.initializeComponent();
        }

        function openEngagementEmailReply(emailId) {
            if (!window.VIS || typeof VIS.Email !== 'function' || typeof VIS.CFrame !== 'function') return;
            fetchModal('GetEmailDetail', { emailId: emailId }, function (err, data) {
                if (err || !data || !data.id) return;
                var tableId  = $self.table_ID || 0;
                var replyTo  = data.direction === 'in' ? (data.fromEmail || '') : (data.toEmail || '');
                var subject  = 'RE: ' + (data.subject || '');
                var bodyHtml = '<br><br><hr>' + (data.body || '');
                var email    = new VIS.Email(replyTo, null, null, currentBpId, true, true, tableId, bodyHtml, subject, null);
                var c        = new VIS.CFrame();
                c.setName(msg('EMail'));
                c.setTitle(msg('EMail'));
                c.hideHeader(true);
                c.setContent(email);
                c.show();
                email.initializeComponent();
            });
        }

        // ── Form submissions ──────────────────────────────────────────────────
        function submitNote(text, inputEl) {
            $.ajax({
                url:  VIS.Application.contextUrl + 'VAS/VAS_105_AccountRightPanel/PostNote',
                type: 'POST',
                data: { bPartnerId: currentBpId, noteText: text },
                async: true,
                success: function (raw) {
                    if (inputEl) inputEl.value = '';
                    // Reload notes section
                    fetchSection('notes', 'GetNotes', {}, null);
                },
                error: function () { /* silent fail */ }
            });
        }

        function submitActivity(actType, subject, date, duration) {
            $.ajax({
                url:  VIS.Application.contextUrl + 'VAS/VAS_105_AccountRightPanel/PostActivity',
                type: 'POST',
                data: { bPartnerId: currentBpId, actType: actType, subject: subject, date: date, duration: duration },
                async: true,
                success: function () {
                    closeModal();
                    fetchSection('timeline', 'GetEngagement', {}, null);
                },
                error: function () { closeModal(); }
            });
        }

        function submitMeeting(title, startDateTime, endDateTime, location, description) {
            $.ajax({
                url:  VIS.Application.contextUrl + 'VAS/VAS_105_AccountRightPanel/PostMeeting',
                type: 'POST',
                data: { bPartnerId: currentBpId, title: title, startDateTime: startDateTime, endDateTime: endDateTime, location: location, description: description },
                async: true,
                success: function () {
                    closeModal();
                    fetchSection('timeline', 'GetEngagement', {}, null);
                },
                error: function () { closeModal(); }
            });
        }

        // ── Dynamic section visibility ────────────────────────────────────────
        // Sections that own an add entry-point (New Task / note composer).
        // These stay visible even when empty, as long as the record is editable.
        function secHasAddEntry(secName) { return secName === 'tasks' || secName === 'timeline'; }

        // Returns the jQuery wrapper element that shows/hides the whole section.
        function secWrap(secName) {
            if (secName === 'tasks') return $root.find('#vas_105_tasks_sec_'    + widgetID);
            return $root.find('#vas_105_wrap_' + secName + '_' + widgetID);
        }

        // Extracts the item count from a section's loaded data object.
        function getSectionItemCount(secName, data) {
            if (!data || !data.items) return 0;
            return data.items.length;
        }

        // Show or hide a section wrapper after its data has been rendered.
        // - Sections with no add entry-point: hide when item count is 0.
        // - Sections with an add entry-point: hide only when non-editable AND empty.
        // - Contacts/Locations: each column is toggled independently; the shared
        //   outer wrapper (wrap_cl) is hidden only when both columns are hidden.
        function applySecVisibility(secName, itemCount) {
            if (secName === 'overview') return;  // overview always visible
            var $wrap = secWrap(secName);
            if (!$wrap.length) return;

            var keep = itemCount > 0 || (secHasAddEntry(secName) && _bpIsEditable);
            $wrap.toggle(keep);

            // Keep the shared contacts+locations row visible unless BOTH columns hide.
            if (secName === 'contacts' || secName === 'locations') {
                var cVis = secWrap('contacts').is(':visible');
                var lVis = secWrap('locations').is(':visible');
                $root.find('#vas_105_wrap_cl_' + widgetID).toggle(cVis || lVis);
            }
        }

        // ── Add-entry-point visibility rule ───────────────────────────────────
        // Rule: hide when not applicable (no record); hide when empty AND record
        // is in a non-editable state; otherwise keep the Add entry point.
        // BPartner is always editable (_bpIsEditable = true), so the second
        // condition never fires — Add buttons are shown whenever a record is loaded.
        function applyAddBtnState() {
            if (!currentBpId) return;  // no record — shells not in DOM
            if (!_bpIsEditable) {
                // Future: if BPartner ever has a non-editable state, hide when empty
                var tkCnt  = _bpSectionCounts['tasks'];
                var engCnt = _bpSectionCounts['timeline'];
                $root.find('#vas_105_btn_new_task_'    + widgetID).toggle(tkCnt  === undefined || tkCnt  > 0);
                $root.find('.vas_105_eng-notecomposer'            ).toggle(engCnt === undefined || engCnt > 0);
                return;
            }
            // Editable: always show both Add entry points
            $root.find('#vas_105_btn_new_task_' + widgetID).show();
            $root.find('.vas_105_eng-notecomposer'        ).show();
        }

        // ── Load all sections ─────────────────────────────────────────────────
        function loadAllSections() {
            if (!currentBpId) { renderNoSelectionState(); return; }

            _bpIsEditable    = true;   // BPartner is always editable
            _bpSectionCounts = {};     // clear stale counts from previous account
            _ordersOffset    = 0;
            _invoicesOffset  = 0;
            _oppsPage        = 0;
            _contractsPage   = 0;

            // Rebuild section shells — renderNoSelectionState() replaces the whole
            // body with a single div, so we must restore shells before AJAX callbacks
            // try to find their target elements via renderSec().
            var $body = $root.find('#vas_105_body_' + widgetID);
            $body.html(buildBodyHtml());
            setupSectionHeaders();

            // Reset scroll to top when navigating to a new record.
            // Staggered resets override any position restoration the platform
            // may apply asynchronously after this call returns.
            var bodyEl = $body[0];
            if (bodyEl) {
                bodyEl.scrollTop = 0;
                setTimeout(function () { if (bodyEl) bodyEl.scrollTop = 0; }, 0);
                setTimeout(function () { if (bodyEl) bodyEl.scrollTop = 0; }, 100);
                setTimeout(function () { if (bodyEl) bodyEl.scrollTop = 0; }, 300);
            }

            // ── Wire task filter toggle ──
            $root.find('#vas_105_tasks_sec_' + widgetID).on('click', '[data-tk-filter]', function (e) {
                e.stopPropagation(); e.stopImmediatePropagation();
                var f = $(this).data('tk-filter');
                if (f === taskFilter) return;
                taskFilter = f;
                $(this).closest('.vas_105_seg').find('.vas_105_segbtn').removeClass('active');
                $(this).addClass('active');
                loadTasks();
            });

            // ── Wire task checkbox (capture phase — fires before browser default) ──
            (function () {
                var bodyEl = $root.find('#vas_105_body_' + widgetID).get(0);
                if (!bodyEl) return;
                bodyEl.addEventListener('click', function (e) {
                    var t = e.target;
                    if (!t || !t.classList || !t.classList.contains('vas_105_tchk') || !t.hasAttribute('data-task-id')) return;
                    e.preventDefault();
                    e.stopPropagation();
                    var isClosed = t.getAttribute('data-is-closed') === 'true';
                    t.checked = isClosed;
                    var tid = parseInt(t.getAttribute('data-task-id'), 10);
                    if (!tid || tid <= 0) return;
                    t.disabled = true;
                    var endpoint = isClosed ? 'ReopenTask' : 'CompleteTask';
                    $.ajax({
                        url:  VIS.Application.contextUrl + 'VAS/VAS_105_AccountRightPanel/' + endpoint,
                        type: 'POST',
                        data: { bPartnerId: currentBpId, taskId: tid },
                        success: function (raw) {
                            if (t.parentNode) t.disabled = false;
                            var res = null;
                            try { res = (typeof raw === 'string') ? jQuery.parseJSON(raw) : raw; } catch (e) {}
                            if (res && res.success && t.parentNode) t.checked = !isClosed;
                            loadTasks();
                        },
                        error: function () {
                            if (t.parentNode) { t.disabled = false; t.checked = isClosed; }
                        }
                    });
                }, true);
            }());

            // ── Wire task row click → edit-task dialog ──
            $root.find('#vas_105_body_' + widgetID).on('click', '.vas_105_taskrow-main[data-task-id]', function (e) {
                e.stopPropagation(); e.stopImmediatePropagation();
                var tid = parseInt($(this).data('task-id'), 10);
                if (!tid || tid <= 0) return;
                if (!window.WSP || !WSP.EditTaskForm || typeof WSP.EditTaskForm.init !== 'function') return;
                var tableId  = $self.table_ID || 0;
                var userId   = (VIS.context && typeof VIS.context.getAD_User_ID  === 'function') ? VIS.context.getAD_User_ID()  : 0;
                var userName = (VIS.context && typeof VIS.context.getAD_UserName === 'function') ? VIS.context.getAD_UserName() : '';
                var $busy = $('<div id="divAptBusy" class="wsp-busy-indicater"></div>');
                $('body').append($busy); $busy.show();
                WSP.EditTaskForm.init(tid, tableId, currentBpId, userId, userName, $busy);
                var refreshed = false;
                var ajaxNs = 'ajaxComplete.va039task' + tid;
                var obs = new MutationObserver(function (mutations) {
                    for (var i = 0; i < mutations.length; i++) {
                        var removed = mutations[i].removedNodes;
                        for (var j = 0; j < removed.length; j++) {
                            var node = removed[j];
                            if (node.id === 'divTaskContinerFrom' ||
                                (node.querySelector && node.querySelector('#divTaskContinerFrom'))) {
                                obs.disconnect();
                                $(document).off(ajaxNs);
                                if (!refreshed) { refreshed = true; loadTasks(); }
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
                        if (!refreshed) { refreshed = true; loadTasks(); }
                    }
                });
            });

            // ── Wire new task button ──
            $root.find('#vas_105_btn_new_task_' + widgetID).off('click').on('click', function (e) {
                e.stopPropagation(); e.stopImmediatePropagation();
                if (!window.VIS || !VIS.AppointmentsForm || typeof VIS.AppointmentsForm.init !== 'function') return;
                var tableId  = $self.table_ID || 0;
                var userId   = (VIS.context && typeof VIS.context.getAD_User_ID  === 'function') ? VIS.context.getAD_User_ID()  : 0;
                var userName = (VIS.context && typeof VIS.context.getAD_UserName === 'function') ? VIS.context.getAD_UserName() : '';

                if (typeof window.$backBtn_ID === 'undefined') { window.$backBtn_ID = $(); }

                var tkNs        = 'ajaxComplete.va039newtk' + Date.now();
                var taskCreated = false;   // true once CreateJson_Task AJAX succeeds
                var formSeen    = false;   // true once the popup is confirmed open in DOM
                var listenersDone = false;
                var tkPrevErr   = window.onerror;
                var tkObs, tkPoll;

                function _cleanup() {
                    if (listenersDone) return;
                    listenersDone = true;
                    window.onerror = tkPrevErr;
                    $(document).off(tkNs);
                    if (tkObs)  { tkObs.disconnect();    tkObs  = null; }
                    if (tkPoll) { clearInterval(tkPoll); tkPoll = null; }
                }

                // Called only when the popup has closed AND a task was actually created.
                function _onPopupClosed() {
                    _cleanup();
                    if (taskCreated) {
                        taskFilter = 'upcoming';
                        loadTasks();
                    }
                }

                // Strategy A: wsptask.js crashes in its AJAX success callback.
                // The task IS saved but ajaxComplete never fires (jQuery aborts the chain).
                // We detect the crash, mark taskCreated, then trigger close handling.
                window.onerror = function (msg, src) {
                    if (src && src.indexOf('wsptask') >= 0 && !listenersDone) {
                        taskCreated = true;
                        setTimeout(_onPopupClosed, 300);
                    }
                    return tkPrevErr ? tkPrevErr.apply(this, arguments) : false;
                };

                VIS.AppointmentsForm.init(tableId, currentBpId, userId, userName, true);

                // Strategy B: task saved cleanly — mark the flag; the popup is still open
                // showing the platform's own success message. We do NOT refresh yet —
                // we wait for the user to close the popup (detected by C/D below).
                $(document).on(tkNs, function (ev, xhr, settings) {
                    if (settings && settings.url && settings.url.indexOf('CreateJson_Task') >= 0) {
                        taskCreated = true;
                    }
                });

                // Strategies C + D share a two-phase approach:
                //   Phase 1 — wait until the popup's busy overlay is in the DOM (formSeen = true)
                //   Phase 2 — watch for that overlay to be removed (popup closed)
                // This prevents false triggers before the form has even opened.

                // Strategy C: MutationObserver
                tkObs = new MutationObserver(function (mutations) {
                    for (var mi = 0; mi < mutations.length; mi++) {
                        // Phase 1: detect form opening via added nodes
                        if (!formSeen) {
                            var added = mutations[mi].addedNodes;
                            for (var ai = 0; ai < added.length; ai++) {
                                var an = added[ai];
                                if (an.id === 'divAptBusy' ||
                                    (an.querySelector && an.querySelector('#divAptBusy'))) {
                                    formSeen = true;
                                    break;
                                }
                            }
                        }
                        // Phase 2: detect form closing via removed nodes
                        if (formSeen) {
                            var removed = mutations[mi].removedNodes;
                            for (var ri = 0; ri < removed.length; ri++) {
                                var node = removed[ri];
                                if (node.id === 'divAptBusy' ||
                                    (node.querySelector && node.querySelector('#divAptBusy'))) {
                                    _onPopupClosed();
                                    return;
                                }
                            }
                        }
                    }
                });
                tkObs.observe(document.body, { childList: true, subtree: true });

                // Strategy D: polling fallback
                tkPoll = setInterval(function () {
                    if (listenersDone) { clearInterval(tkPoll); return; }
                    var busyExists = !!document.getElementById('divAptBusy');
                    var formExists = !!document.getElementById('divTaskContinerFrom') || !!$('.wsp-task-form').length;
                    // Phase 1: wait until form is open
                    if (!formSeen) {
                        if (busyExists || formExists) { formSeen = true; }
                        return;
                    }
                    // Phase 2: trigger only after form has visibly closed
                    if (!busyExists && !formExists) {
                        clearInterval(tkPoll); tkPoll = null;
                        _onPopupClosed();
                    }
                }, 500);

                setTimeout(function () { _cleanup(); }, 300000);
            });

            // ── Wire engagement section inline note composer ──
            $root.find('#vas_105_eng_note_post_' + widgetID).off('click').on('click', function (e) {
                e.stopPropagation();
                var $inp = $root.find('#vas_105_eng_note_input_' + widgetID);
                var txt = $.trim($inp.val());
                if (!txt) return;
                var $btn = $(this).prop('disabled', true);
                $.ajax({
                    url:  VIS.Application.contextUrl + 'VAS/VAS_105_AccountRightPanel/PostNote',
                    type: 'POST',
                    data: { bPartnerId: currentBpId, noteText: txt },
                    async: true,
                    success: function () {
                        $inp.val('');
                        $btn.prop('disabled', false);
                        fetchSection('timeline', 'GetEngagement', {}, null);
                    },
                    error: function () { $btn.prop('disabled', false); }
                });
            });

            // Reset all section states
            for (var si = 0; si < SECTIONS.length; si++) {
                sectionState[SECTIONS[si]] = { loading: false, error: null, data: null, loaded: false };
            }

            // Overview is fetched immediately (paint first)
            fetchSection('overview', 'GetOverview', {}, function (data) {
                renderSec('overview');
            });

            // Remaining sections: fetch with short stagger so overview paints first
            window.setTimeout(function () {
                fetchSection('contacts',  'GetContacts',  {}, null);
                fetchSection('locations', 'GetLocations', {}, null);
            }, 50);

            window.setTimeout(function () {
                fetchSection('opps',      'GetOpportunities', {}, null);
                fetchSection('contracts', 'GetContracts',     {}, null);
            }, 100);

            window.setTimeout(function () {
                fetchSection('tickets', 'GetTickets', { state: 'open', pageOffset: 0, pageSize: 5 }, null);
            }, 150);

            window.setTimeout(function () {
                fetchSection('orders',   'GetOrders',   { pageOffset: 0, pageSize: PAGE_SIZE_INLINE }, null);
                fetchSection('invoices', 'GetInvoices', { pageOffset: 0, pageSize: PAGE_SIZE_INLINE }, null);
                fetchSection('tasks',    'GetTasks',    {}, null);
            }, 200);

            window.setTimeout(function () {
                fetchSection('projects',  'GetProjects', {}, null);
                fetchSection('timeline',  'GetEngagement', {}, null);
            }, 250);
        }

        function renderNoSelectionState() {
            $root.find('#vas_105_body_' + widgetID).html(
                '<div class="vas_105_acct-nosel">' +
                '<i class="ti ti-user-circle" style="font-size:3em;opacity:0.3;"></i>' +
                '<span>' + esc(msg('VAS_105_SelectAccount')) + '</span>' +
                '</div>'
            );
        }

        // ── Build the section shells ──────────────────────────────────────────
        function buildBodyHtml() {
            var wid = widgetID;

            return (
                // Overview (identity + key facts + company info)
                '<div id="' + secId('overview') + '"></div>' +

                // Contacts & Locations side by side
                '<div class="vas_105_acct-sec" id="vas_105_wrap_cl_' + wid + '">' +
                  '<div class="vas_105_acct-sidegrid">' +
                    '<div id="vas_105_wrap_contacts_' + wid + '">' +
                      '<div class="vas_105_acct-sechead">' +
                        '<span class="vas_105_acct-sectitle" id="vas_105_lbl_contacts_' + wid + '"></span>' +
                        '<span class="vas_105_acct-seccount" id="' + secId('contacts') + '_cnt"></span>' +
                      '</div>' +
                      '<div id="' + secId('contacts') + '"></div>' +
                    '</div>' +
                    '<div id="vas_105_wrap_locations_' + wid + '">' +
                      '<div class="vas_105_acct-sechead">' +
                        '<span class="vas_105_acct-sectitle" id="vas_105_lbl_locations_' + wid + '"></span>' +
                        '<span class="vas_105_acct-seccount" id="' + secId('locations') + '_cnt"></span>' +
                      '</div>' +
                      '<div id="' + secId('locations') + '"></div>' +
                    '</div>' +
                  '</div>' +
                '</div>' +

                // Opportunities
                '<div class="vas_105_acct-sec" id="vas_105_wrap_opps_' + wid + '">' +
                  '<div class="vas_105_acct-sechead">' +
                    '<span class="vas_105_acct-sectitle" id="vas_105_lbl_opps_' + wid + '"></span>' +
                    '<span class="vas_105_acct-seccount" id="' + secId('opps') + '_cnt"></span>' +
                  '</div>' +
                  '<div id="' + secId('opps') + '"></div>' +
                '</div>' +

                // Contracts
                '<div class="vas_105_acct-sec" id="vas_105_wrap_contracts_' + wid + '">' +
                  '<div class="vas_105_acct-sechead">' +
                    '<span class="vas_105_acct-sectitle" id="vas_105_lbl_contracts_' + wid + '"></span>' +
                    '<span class="vas_105_acct-seccount" id="' + secId('contracts') + '_cnt"></span>' +
                  '</div>' +
                  '<div id="' + secId('contracts') + '"></div>' +
                '</div>' +

                // Support tickets
                '<div class="vas_105_acct-sec" id="vas_105_wrap_tickets_' + wid + '">' +
                  '<div class="vas_105_acct-sechead">' +
                    '<span class="vas_105_acct-sectitle" id="vas_105_lbl_tickets_' + wid + '"></span>' +
                    '<button class="vas_105_acct-btn--link" id="vas_105_btn_past_tick_' + wid + '" type="button">' +
                      '<i class="ti ti-history" style="font-size:0.875em;"></i>' +
                    '</button>' +
                  '</div>' +
                  '<div id="' + secId('tickets') + '"></div>' +
                '</div>' +

                // Orders
                '<div class="vas_105_acct-sec" id="vas_105_wrap_orders_' + wid + '">' +
                  '<div class="vas_105_acct-sechead">' +
                    '<span class="vas_105_acct-sectitle" id="vas_105_lbl_orders_' + wid + '"></span>' +
                    '<button class="vas_105_acct-btn--link" id="vas_105_btn_all_ord_' + wid + '" type="button">' +
                      '<i class="ti ti-shopping-cart" style="font-size:0.875em;"></i>' +
                    '</button>' +
                  '</div>' +
                  '<div id="' + secId('orders') + '"></div>' +
                '</div>' +

                // Invoices
                '<div class="vas_105_acct-sec" id="vas_105_wrap_invoices_' + wid + '">' +
                  '<div class="vas_105_acct-sechead">' +
                    '<span class="vas_105_acct-sectitle" id="vas_105_lbl_invoices_' + wid + '"></span>' +
                    '<button class="vas_105_acct-btn--link" id="vas_105_btn_all_inv_' + wid + '" type="button">' +
                      '<i class="ti ti-receipt" style="font-size:0.875em;"></i>' +
                    '</button>' +
                  '</div>' +
                  '<div id="' + secId('invoices') + '"></div>' +
                '</div>' +

                // Projects
                '<div class="vas_105_acct-sec" id="vas_105_wrap_projects_' + wid + '">' +
                  '<div class="vas_105_acct-sechead">' +
                    '<span class="vas_105_acct-sectitle" id="vas_105_lbl_projects_' + wid + '"></span>' +
                  '</div>' +
                  '<div id="' + secId('projects') + '"></div>' +
                '</div>' +

                // Tasks
                '<div class="vas_105_acct-sec" id="vas_105_tasks_sec_' + wid + '">' +
                  '<div class="vas_105_acct-sechead">' +
                    '<span class="vas_105_acct-sectitle" id="vas_105_lbl_tasks_' + wid + '"></span>' +
                    '<span class="vas_105_acct-seccount" id="' + secId('tasks') + '_cnt"></span>' +
                    '<div style="display:flex;align-items:center;gap:0.5em;margin-left:auto;">' +
                      '<div class="vas_105_seg" style="width:auto;margin-bottom:0;">' +
                        '<button class="vas_105_segbtn active" data-tk-filter="upcoming" id="vas_105_tkf_upcoming_' + wid + '"></button>' +
                        '<button class="vas_105_segbtn" data-tk-filter="previous" id="vas_105_tkf_previous_' + wid + '"></button>' +
                      '</div>' +
                      '<button class="vas_105_acct-btn vas_105_acct-btn--link" id="vas_105_btn_new_task_' + wid + '" type="button"></button>' +
                    '</div>' +
                  '</div>' +
                  '<div id="' + secId('tasks') + '"></div>' +
                '</div>' +

                // Engagement timeline
                '<div class="vas_105_acct-sec" id="vas_105_wrap_timeline_' + wid + '">' +
                  '<div class="vas_105_acct-eng">' +
                    '<div class="vas_105_acct-eng__head">' +
                      '<div style="display:flex;align-items:center;gap:0.625em;">' +
                        '<span class="vas_105_acct-eng__icon"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/></svg></span>' +
                        '<span class="vas_105_acct-sectitle" id="vas_105_lbl_timeline_' + wid + '"></span>' +
                      '</div>' +
                    '</div>' +
                    '<div class="vas_105_acct-eng__inner">' +
                      '<div id="' + secId('timeline') + '"></div>' +
                    '</div>' +
                    '<div class="vas_105_eng-notecomposer">' +
                      '<div class="vas_105_eng-notecomposer-hint">' +
                        '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                        '<path d="M14.5 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7.5L14.5 2z"/><polyline points="14 2 14 8 20 8"/></svg>' +
                      '</div>' +
                      '<textarea id="vas_105_eng_note_input_' + wid + '" class="vas_105_eng-notecomposer-input" rows="1"></textarea>' +
                      '<button class="vas_105_acct-btn vas_105_acct-btn--primary" id="vas_105_eng_note_post_' + wid + '" type="button"></button>' +
                    '</div>' +
                  '</div>' +
                '</div>' +

                ''
            );
        }

        // ── Set section label text ────────────────────────────────────────────
        function setupSectionHeaders() {
            var wid = widgetID;
            var lblMap = {
                'contacts': 'VAS_105_ContactsRoles', 'locations': 'VAS_105_Locations',
                'opps': 'VAS_105_Opportunities', 'contracts': 'VAS_105_Contracts',
                'tickets': 'VAS_105_SupportTickets', 'tasks': 'VAS_105_Tasks',
                'timeline': 'VAS_105_EngagementTimeline',
                'orders': 'Orders', 'invoices': 'Invoices',
                'projects': 'VAS_105_Projects'
            };
            for (var key in lblMap) {
                if (!lblMap.hasOwnProperty(key)) continue;
                $root.find('#vas_105_lbl_' + key + '_' + wid).text(msg(lblMap[key]));
            }

            var $btnPastTick = $root.find('#vas_105_btn_past_tick_' + wid);
            if ($btnPastTick.length) {
                $btnPastTick.text('').append(
                    $('<i class="ti ti-history" style="font-size:0.875em;"></i>'),
                    document.createTextNode(msg('VAS_105_ViewPastTickets'))
                );
                $btnPastTick.off('click').on('click', function () { openPastTickets(); });
            }
            var $btnAllOrd = $root.find('#vas_105_btn_all_ord_' + wid);
            if ($btnAllOrd.length) {
                $btnAllOrd.text('').append(
                    $('<i class="ti ti-shopping-cart" style="font-size:0.875em;"></i>'),
                    document.createTextNode(msg('VAS_105_ViewAllOrders'))
                );
                $btnAllOrd.off('click').on('click', function () { openAllOrders(); });
            }
            var $btnAllInv = $root.find('#vas_105_btn_all_inv_' + wid);
            if ($btnAllInv.length) {
                $btnAllInv.text('').append(
                    $('<i class="ti ti-receipt" style="font-size:0.875em;"></i>'),
                    document.createTextNode(msg('VAS_105_ViewAllInvoices'))
                );
                $btnAllInv.off('click').on('click', function () { openAllInvoices(); });
            }

            // ── Task section labels & controls ──
            $root.find('#vas_105_tkf_upcoming_' + wid).text(msg('VAS_105_Upcoming'));
            $root.find('#vas_105_tkf_previous_' + wid).text(msg('VIS_Previous'));
            var $btnNewTask = $root.find('#vas_105_btn_new_task_' + wid);
            if ($btnNewTask.length) {
                $btnNewTask.text('').append(
                    $('<i class="ti ti-plus" style="font-size:0.875em;margin-right:0.3em;"></i>'),
                    document.createTextNode(msg('VAS_105_NewTask'))
                );
            }

            // ── Engagement inline note composer labels ──
            $root.find('#vas_105_eng_note_input_' + wid).attr('placeholder', msg('VAS_105_AddNote'));
            $root.find('#vas_105_eng_note_post_' + wid).text(msg('Send'));
        }

        // ── Cleanup ───────────────────────────────────────────────────────────
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
            currentBpId = 0;
        };

        // Reset all state and show the no-selection placeholder (mirrors VAS_065 clear pattern)
        this.clear = function () {
            abortAll();
            currentBpId = 0;
            for (var si = 0; si < SECTIONS.length; si++) {
                sectionState[SECTIONS[si]] = { loading: false, error: null, data: null, loaded: false };
            }
            if ($root) renderNoSelectionState();
        };

        // ── Public: load a specific account ──────────────────────────────────
        this.loadAccount = function (bPartnerId) {
            var newId = parseInt(bPartnerId, 10) || 0;
            if (newId === currentBpId) return;
            abortAll();
            currentBpId = newId;
            loadAllSections();
            // New record -> always start at the top (the scroll position must not
            // carry over from the previously selected record).
            //if ($root && $root[0]) {
            //    $root.scrollTop(0);
            //}
        };

        this.refreshWidget = function () {
            if (currentBpId > 0) loadAllSections();
        };

        this.sizeChanged = function () { /* em-based; no layout recalculation needed */ };

        // ── Initialize ────────────────────────────────────────────────────────
        this.Initialize = function () {
            widgetID = (this.widgetInfo && this.widgetInfo.AD_UserHomeWidgetID)
                ? this.widgetInfo.AD_UserHomeWidgetID
                : ($self.windowNo || 0);

            var wid = widgetID;

            $root = $(
                '<div class="vas_105_acct-shell" id="vas_105_acct_shell_' + wid + '">' +
                  '<div class="vas_105_acct-panel">' +
                    '<div class="vas_105_acct-body" id="vas_105_body_' + wid + '">' +
                    '</div>' +
                  '</div>' +
                  // Modal overlay (scoped inside shell so z-index stacking works)
                  '<div class="vas_105_acct-overlay" id="vas_105_overlay_' + wid + '" role="dialog" aria-modal="true">' +
                    '<div class="vas_105_acct-modal" id="vas_105_modal_' + wid + '">' +
                      '<div class="vas_105_acct-modal__head">' +
                        '<div style="display:flex;align-items:center;gap:0.5em;min-width:0;">' +
                          '<span class="vas_105_acct-modal__title" id="vas_105_mtitle_' + wid + '"></span>' +
                          '<span class="vas_105_acct-modal__meta"  id="vas_105_mmeta_'  + wid + '"></span>' +
                        '</div>' +
                        '<button class="vas_105_acct-iconbtn" id="vas_105_mclose_' + wid + '" aria-label="Close dialog">' +
                          '<i class="ti ti-x" style="font-size:1.25em;"></i>' +
                        '</button>' +
                      '</div>' +
                      '<div class="vas_105_acct-modal__body" id="vas_105_mbody_' + wid + '"></div>' +
                      '<div class="vas_105_acct-modal__foot" id="vas_105_mfoot_' + wid + '"></div>' +
                    '</div>' +
                  '</div>' +
                '</div>'
            );


            // Wire modal close button
            $root.find('#vas_105_mclose_' + wid).on('click', function () { closeModal(); });

            // Esc key closes modal
            $(document).on('keydown.vas_105_acct_' + wid, function (e) {
                if (e.key === 'Escape' || e.keyCode === 27) closeModal();
            });

            // Overlay backdrop click closes modal
            $root.find('#vas_105_overlay_' + wid).on('click', function (e) {
                if (e.target === this) closeModal();
            });

            // Wire WhatsApp stat card click → open WhatsApp conversation modal
            $root.on('click', '[data-action="openChat"]', function (e) {
                e.stopPropagation(); e.stopImmediatePropagation();
                openWhatsAppModal();
            });

            // Wire chat timeline item click → open WhatsApp conversation for that topic
            $root.on('click', '[data-chat-topic-id]', function (e) {
                e.stopPropagation(); e.stopImmediatePropagation();
                var tid = parseInt($(this).data('chat-topic-id'), 10);
                if (tid > 0) openWhatsAppModal(tid);
            });

            // Wire meeting card click → open meeting detail modal
            $root.on('click', '[data-meeting-id]', function (e) {
                e.stopPropagation(); e.stopImmediatePropagation();
                var mid = parseInt($(this).data('meeting-id'), 10);
                if (mid > 0) openMeetingDetailModal(mid);
            });

            // Wire note card click → open note detail modal
            $root.on('click', '[data-note-id]', function (e) {
                e.stopPropagation(); e.stopImmediatePropagation();
                var nid = parseInt($(this).data('note-id'), 10);
                if (nid > 0) openNoteDetailModal(nid);
            });

            // Wire email card click → reply if engagement email, otherwise detail modal
            $root.on('click', '[data-email-id]', function (e) {
                e.stopPropagation(); e.stopImmediatePropagation();
                var eid = parseInt($(this).data('email-id'), 10);
                if (eid <= 0) return;
                if ($(this).data('eng-email')) {
                    openEngagementEmailReply(eid);
                } else {
                    openEmailDetailModal(eid);
                }
            });

            // Wire send-email action → open standard send-email window
            $root.on('click', '[data-action="sendEmail"]', function (e) {
                e.stopPropagation(); e.stopImmediatePropagation();
                openSendEmailModal();
            });

            // Wire contact mail button → open standard VIS Email compose pre-addressed to the contact
            $root.on('click', '[data-action="contactEmail"]', function (e) {
                e.stopPropagation(); e.stopImmediatePropagation();
                openContactEmailCompose($(this).data('email') || '');
            });

            // Wire make-call action → initiate call via VA048 or tel: fallback
            $root.on('click', '[data-action="makeCall"]', function (e) {
                e.stopPropagation(); e.stopImmediatePropagation();
                var number = $(this).data('mobile');
                if (!number) return;
                if (window.VA048 && VA048.Apps && typeof VA048.Apps.GetCallingInstance === 'function') {
                    var tableId = $self.table_ID || 0;
                    VA048.Apps.GetCallingInstance(true, {
                        tonumbers:     number,
                        username:      '',
                        userimg:       '',
                        isconference:  false,
                        reftableid:    -1,
                        refrecordid:   -1,
                        windowno:      0,
                        windowid:      0,
                        tableid:       tableId,
                        recordid:      currentBpId || 0,
                        withrecording: false,
                        withcall:      true
                    }, false);
                } else {
                    window.location = 'tel:' + number;
                }
            });

// Build body section shells and set labels
            $root.find('#vas_105_body_' + wid).html(buildBodyHtml());
            setupSectionHeaders();

            // If widgetInfo provides an initial bPartnerId, load it
            if (this.widgetInfo && this.widgetInfo.bPartnerId) {
                $self.loadAccount(this.widgetInfo.bPartnerId);
            }
        };

        this.getRoot = function () { return $root; };
    };

    // ── Prototype ─────────────────────────────────────────────────────────────

    /** Tab panel interface — called by initializeTabPanel when the user opens the panel */
    VAS.VAS_105_AccountRightPanel.prototype.startPanel = function (windowNo, curTab) {
        this.windowNo = windowNo;
        this.curTab   = curTab;
        this.table_ID = curTab ? curTab.getAD_Table_ID() : 0;
        this.Initialize();
    };

    /** Tab panel interface — called when the user navigates to a record */
    VAS.VAS_105_AccountRightPanel.prototype.refreshPanelData = function (recordID, selectedRow) {
        if (selectedRow == undefined || recordID <= 0) { this.clear(); return; }
        this.record_ID   = recordID;
        this.selectedRow = selectedRow;
        this.loadAccount(recordID);
    };

    /** Tab panel interface — called when the panel is resized */
    VAS.VAS_105_AccountRightPanel.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /** Dashboard widget interface — called when used as a home widget */
    VAS.VAS_105_AccountRightPanel.prototype.widgetSizeChange = function (width) {
        this.panelWidth = width;
    };

    VAS.VAS_105_AccountRightPanel.prototype.refreshWidget = function () {
        if (selectedRow == undefined || recordID <= 0) { this.clear(); return; }
        this.record_ID = recordID;
        this.selectedRow = selectedRow;
        this.refreshWidget();
    };

    VAS.VAS_105_AccountRightPanel.prototype.init = function (windowNo, frame) {
        this.frame      = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo   = windowNo;
        this.Initialize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_105_AccountRightPanel.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) this.listener.widgetFirevalueChanged(value);
    };

    VAS.VAS_105_AccountRightPanel.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_105_AccountRightPanel.prototype.dispose = function () {
        if (typeof this.cleanup === 'function') this.cleanup();
        var keyId = (this.widgetInfo && this.widgetInfo.AD_UserHomeWidgetID)
            ? this.widgetInfo.AD_UserHomeWidgetID
            : this.windowNo;
        if (keyId != null) $(document).off('keydown.vas_105_acct_' + keyId);
        if (this.frame) this.frame.dispose();
        this.record_ID = this.table_ID = 0;
        this.curTab = this.frame = this.windowNo = null;
    };

})(VAS, jQuery);
