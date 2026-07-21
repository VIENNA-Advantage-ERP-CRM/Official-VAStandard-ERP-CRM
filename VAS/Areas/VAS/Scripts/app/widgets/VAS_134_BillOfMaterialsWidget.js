/**
 * Bill of Materials Widget (Product Master dashboard)
 * Widget number 134.
 * Widget size: 2 columns x 1 row.
 * Dual-stat KPI tile (two independent real buttons) with two drill-down
 * modals sharing one modal/pager implementation (built once, configured
 * twice - see MODALS below), matching the doc's explicit "do not duplicate
 * event logic" requirement. Left stat = distinct active products used as
 * BOM components (opens "Products in BOM"); right stat = active BOMs whose
 * parent product is not verified (opens "BOM Verification"). No JS pixel
 * measurement decides row fit: row height and the list's max-height are
 * both plain em against the same fluid modal font-size (see .MPC-bom-row /
 * .MPC-bom-list in the CSS), so "8 rows fit without a scrollbar" holds by
 * construction at every screen size. Read-only. Plain DOM internals (no
 * jQuery in the widget logic); jQuery is used only at the framework
 * boundary.
 * Backend - VAS_134_BillOfMaterialsWidget/GetSummary, GetProductsInBom,
 *           GetVerification
 * Summary Message Table
 *  # | Current Text                                        | Message Key
 * ---+------------------------------------------------------+------------------------
 *  1 | Bill of Materials                                   | VAS_134_BOM_Title
 *  2 | In BOM / Pending verify                             | VAS_134_BOM_InBom / VAS_134_BOM_PendingVerify
 *  3 | View products in BOM / View BOMs pending verification | VAS_134_BOM_AriaProducts / VAS_134_BOM_AriaVerification
 *  4 | Products in BOM                                     | VAS_134_BOM_ProductsTitle
 *  5 | Products used as active components in at least one active bill of materials. | VAS_134_BOM_ProductsIntro
 *  6 | Products                                            | VAS_134_BOM_ProductsSection
 *  7 | {0} products                                        | VAS_134_BOM_ProductsSummary
 *  8 | {0} BOM / {0} BOMs                                  | VAS_134_BOM_BomCount / VAS_134_BOM_BomCountPlural
 *  9 | No active products are used as BOM components.      | VAS_134_BOM_ProductsEmpty
 * 10 | BOM Verification                                    | VAS_134_BOM_VerifyTitle
 * 11 | Verification status across active BOMs · not verified listed first. | VAS_134_BOM_VerifyIntro
 * 12 | BOMs                                                | VAS_134_BOM_VerifySection
 * 13 | {0} BOMs · {1} not verified                         | VAS_134_BOM_VerifySummary
 * 14 | Verified / Not verified                             | VAS_134_BOM_Verified / VAS_134_BOM_NotVerified
 * 15 | Rev {0} · {1} components · updated by {2} · updated {3} | VAS_134_BOM_VerifyMeta
 * 16 | Unknown user                                        | VAS_134_BOM_UnknownUser
 * 17 | No active BOMs were found.                          | VAS_134_BOM_VerifyEmpty
 * 18 | Retry                                                | VAS_134_BOM_Retry
 * 19 | Showing / of                                         | VAS_Showing / VAS_Of (shared global keys)
 * 20 | Couldn't load / Close                                | VAS_CouldntLoad / Close (shared global keys)
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    function ensureDashInlineSizeVar(rootEl) {
        if (window.__vasDashInlineSizeObserver) { return; }
        if (typeof ResizeObserver === 'undefined' || !rootEl || !rootEl.closest) { return; }

        var container = rootEl.closest('.vis-widget-container, [data-dashboard-container]');
        if (!container) { return; }

        var write = function () {
            document.documentElement.style.setProperty('--dash-inline-size', container.clientWidth + 'px');
        };

        window.__vasDashInlineSizeObserver = new ResizeObserver(write);
        window.__vasDashInlineSizeObserver.observe(container);
        write();
    }

    // Fixed page size (the spec's max). No JS measurement adjusts this -
    // .MPC-bom-row / .MPC-bom-list are sized in em so 8 rows always fit
    // without a scrollbar, by CSS construction, at every screen size.
    var PAGE_SIZE = 8;

    VAS.VAS_134_BillOfMaterialsWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var root = document.createElement('div');
        root.className = 'MPC-bom-root';

        var state = {
            productsInBom: 0,
            pendingVerify: 0
        };

        var els = {};
        var summaryController = null;
        var lastFocusedEl = null;

        // Per-modal request state, keyed the same as MODALS below.
        var modalState = {
            products: { offset: 0, total: 0, rows: [], controller: null, token: 0 },
            verify: { offset: 0, total: 0, notVerifiedCount: 0, rows: [], controller: null, token: 0 }
        };

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function format(key, fallback) {
            var text = lbl(key, fallback);
            for (var i = 2; i < arguments.length; i++) {
                text = text.replace('{' + (i - 2) + '}', arguments[i]);
            }
            return text;
        }

        function el(tag, className, text) {
            var node = document.createElement(tag);
            if (className) { node.className = className; }
            if (text != null) { node.textContent = text; }
            return node;
        }

        function svg(paths) {
            var s = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' + paths + '</svg>';
            var span = document.createElement('span');
            span.innerHTML = s; // static, developer-authored SVG only
            return span.firstChild;
        }

        function formatDate(value) {
            if (!value) { return ''; }
            var iso = String(value).replace(' ', 'T');
            var date = new Date(iso);
            if (isNaN(date.getTime())) { return String(value); }
            return date.toLocaleDateString(window.navigator.language, { day: 'numeric', month: 'long', year: 'numeric' });
        }

        function formatRelativeTime(value) {
            if (!value) { return ''; }
            var iso = String(value).replace(' ', 'T');
            var date = new Date(iso);
            if (isNaN(date.getTime())) { return String(value); }
            var diffMs = Date.now() - date.getTime();
            var diffMin = Math.max(0, Math.floor(diffMs / 60000));
            if (diffMin < 1) { return lbl('VAS_134_BOM_JustNow', 'Just now'); }
            if (diffMin < 60) { return format('VAS_134_BOM_MinsAgo', '{0}m ago', diffMin); }
            var diffHour = Math.floor(diffMin / 60);
            if (diffHour < 24) { return format('VAS_134_BOM_HoursAgo', '{0}h ago', diffHour); }
            var diffDay = Math.max(0, Math.floor(diffHour / 24));
            var dayWord = diffDay === 1 ? lbl('VAS_134_BOM_DayWord', 'day') : lbl('VAS_134_BOM_DaysWord', 'days');
            return format('VAS_134_BOM_DaysAgo', '{0} {1} ago', diffDay, dayWord);
        }

        /* ---- Build the static DOM once ---- */
        function build() {
            var card = el('div', 'MPC-bom-card');
            card.appendChild(el('span', 'MPC-bom-title', lbl('VAS_134_BOM_Title', 'Bill of Materials')));

            var dual = el('div', 'MPC-bom-dual');

            var leftBtn = el('button', 'MPC-bom-stat');
            leftBtn.type = 'button';
            leftBtn.setAttribute('aria-label', lbl('VAS_134_BOM_AriaProducts', 'View products in BOM'));
            var leftValue = el('div', 'MPC-bom-value', '…');
            leftBtn.appendChild(leftValue);
            leftBtn.appendChild(el('div', 'MPC-bom-meta', lbl('VAS_134_BOM_InBom', 'In BOM')));
            leftBtn.addEventListener('click', function () { openModal('products', leftBtn); });

            var divider = el('div', 'MPC-bom-divider');

            var rightBtn = el('button', 'MPC-bom-stat');
            rightBtn.type = 'button';
            rightBtn.setAttribute('aria-label', lbl('VAS_134_BOM_AriaVerification', 'View BOMs pending verification'));
            var rightValue = el('div', 'MPC-bom-value MPC-bom-warn', '…');
            rightBtn.appendChild(rightValue);
            rightBtn.appendChild(el('div', 'MPC-bom-meta', lbl('VAS_134_BOM_PendingVerify', 'Pending verify')));
            rightBtn.addEventListener('click', function () { openModal('verify', rightBtn); });

            dual.appendChild(leftBtn);
            dual.appendChild(divider);
            dual.appendChild(rightBtn);
            card.appendChild(dual);

            root.appendChild(card);
            els.leftValue = leftValue;
            els.rightValue = rightValue;

            buildModal('products', lbl('VAS_134_BOM_ProductsTitle', 'Products in BOM'));
            buildModal('verify', lbl('VAS_134_BOM_VerifyTitle', 'BOM Verification'));
        }

        function buildModal(key, titleText) {
            var overlay = el('div', 'MPC-bom-overlay');
            overlay.setAttribute('aria-hidden', 'true');
            var panel = el('div', 'MPC-bom-panel');
            panel.setAttribute('role', 'dialog');
            panel.setAttribute('aria-modal', 'true');
            var titleId = 'MPC-bom-title-' + key + '-' + Math.random().toString(36).slice(2);
            panel.setAttribute('aria-labelledby', titleId);

            var head = el('div', 'MPC-bom-p-head');
            var title = el('span', 'MPC-bom-p-title', titleText);
            title.id = titleId;
            var close = el('button', 'MPC-bom-p-close');
            close.type = 'button';
            close.setAttribute('aria-label', lbl('Close', 'Close'));
            close.appendChild(svg('<path d="M18 6 6 18"/><path d="m6 6 12 12"/>'));
            close.addEventListener('click', function () { closeModal(key); });
            head.appendChild(title);
            head.appendChild(close);

            var pbody = el('div', 'MPC-bom-p-body');
            var intro = el('p', 'MPC-bom-intro');
            var sectionHead = el('div', 'MPC-bom-section-head');
            var sectionTitleText = key === 'products'
                ? lbl('VAS_134_BOM_ProductsSection', 'Products')
                : lbl('VAS_134_BOM_VerifySection', 'BOMs');
            sectionHead.appendChild(el('span', 'MPC-bom-section-title', sectionTitleText));
            var summary = el('span', 'MPC-bom-section-summary');
            sectionHead.appendChild(summary);
            var list = el('div', 'MPC-bom-list');
            pbody.appendChild(intro);
            pbody.appendChild(sectionHead);
            pbody.appendChild(list);

            var foot = el('div', 'MPC-bom-p-foot');
            var helper = el('span', 'MPC-bom-helper');
            var pager = el('span', 'MPC-bom-pager');
            var prev = el('button', 'MPC-bom-pgbtn');
            prev.type = 'button';
            prev.setAttribute('aria-label', lbl('VAS_PreviousPage', 'Previous page'));
            prev.appendChild(svg('<path d="m15 18-6-6 6-6"/>'));
            var pageText = el('span', 'MPC-bom-pgtext', '1 ' + lbl('VAS_Of', 'of') + ' 1');
            var next = el('button', 'MPC-bom-pgbtn');
            next.type = 'button';
            next.setAttribute('aria-label', lbl('VAS_NextPage', 'Next page'));
            next.appendChild(svg('<path d="m9 18 6-6-6-6"/>'));
            prev.addEventListener('click', function () {
                var st = modalState[key];
                if (st.offset <= 0) { return; }
                loadPage(key, Math.max(0, st.offset - PAGE_SIZE));
            });
            next.addEventListener('click', function () {
                var st = modalState[key];
                var nextOffset = st.offset + PAGE_SIZE;
                if (nextOffset >= st.total) { return; }
                loadPage(key, nextOffset);
            });
            pager.appendChild(prev);
            pager.appendChild(pageText);
            pager.appendChild(next);
            foot.appendChild(helper);
            foot.appendChild(pager);

            panel.appendChild(head);
            panel.appendChild(pbody);
            panel.appendChild(foot);
            overlay.appendChild(panel);

            overlay.addEventListener('mousedown', function (e) { if (e.target === overlay) { closeModal(key); } });
            overlay.addEventListener('keydown', function (e) {
                if (e.key === 'Escape') { closeModal(key); return; }
                if (e.key === 'Tab') {
                    var focusables = panel.querySelectorAll('button');
                    if (!focusables.length) { return; }
                    var first = focusables[0], last = focusables[focusables.length - 1];
                    if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
                    else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
                }
            });

            document.body.appendChild(overlay);
            els[key] = {
                overlay: overlay,
                panel: panel,
                intro: intro,
                summary: summary,
                list: list,
                helper: helper,
                pageText: pageText,
                prev: prev,
                next: next,
                close: close
            };
        }

        /* ---- Summary (tile) ---- */
        function loadSummary() {
            if (summaryController && typeof summaryController.abort === 'function') {
                try { summaryController.abort(); } catch (ignored) { }
            }
            summaryController = (typeof AbortController !== 'undefined') ? new AbortController() : null;

            var url = VIS.Application.contextUrl + 'VAS_134_BillOfMaterialsWidget/GetSummary';
            fetch(url, {
                method: 'GET',
                credentials: 'same-origin',
                signal: summaryController ? summaryController.signal : undefined
            }).then(function (res) { return res.text(); }).then(function (text) {
                var data = parseResponse(text);
                if (!data || data.error) { showSummaryError(); return; }
                state.productsInBom = data.productsInBom || 0;
                state.pendingVerify = data.pendingVerify || 0;
                renderSummary();
            }).catch(function (err) {
                if (err && err.name === 'AbortError') { return; }
                showSummaryError();
            });
        }

        function renderSummary() {
            els.leftValue.textContent = String(state.productsInBom);
            els.rightValue.textContent = String(state.pendingVerify);
        }

        function showSummaryError() {
            els.leftValue.textContent = '—';
            els.rightValue.textContent = '—';
        }

        function parseResponse(text) {
            var data = text;
            try {
                if (typeof data === 'string' && data.length) { data = JSON.parse(data); }
                if (typeof data === 'string' && data.length) { data = JSON.parse(data); }
            } catch (e) { return null; }
            return data || {};
        }

        /* ---- Shared modal logic (built once, used by both stats) ---- */
        var MODALS = {
            products: {
                endpoint: 'GetProductsInBom',
                introText: lbl('VAS_134_BOM_ProductsIntro', 'Products used as active components in at least one active bill of materials.'),
                emptyText: lbl('VAS_134_BOM_ProductsEmpty', 'No active products are used as BOM components.'),
                buildRow: buildProductRow,
                summaryText: function (st) {
                    return format('VAS_134_BOM_ProductsSummary', '{0} products', st.total);
                }
            },
            verify: {
                endpoint: 'GetVerification',
                introText: lbl('VAS_134_BOM_VerifyIntro', 'Verification status across active BOMs · not verified listed first.'),
                emptyText: lbl('VAS_134_BOM_VerifyEmpty', 'No active BOMs were found.'),
                buildRow: buildVerifyRow,
                summaryText: function (st) {
                    return format('VAS_134_BOM_VerifySummary', '{0} BOMs · {1} not verified', st.total, st.notVerifiedCount);
                }
            }
        };

        function buildProductRow(row) {
            var rowEl = el('div', 'MPC-bom-row');
            var left = el('div', 'MPC-bom-row-left');
            left.appendChild(el('div', 'MPC-bom-row-primary', row.productName || ''));
            var code = row.productCode && String(row.productCode).trim() ? row.productCode : '—';
            var category = row.productCategory && String(row.productCategory).trim() ? row.productCategory : '—';
            left.appendChild(el('div', 'MPC-bom-row-meta', code + ' · ' + category));
            rowEl.appendChild(left);

            var right = el('div', 'MPC-bom-row-right');
            var bomCount = row.bomCount || 0;
            var bomWord = bomCount === 1
                ? format('VAS_134_BOM_BomCount', '{0} BOM', bomCount)
                : format('VAS_134_BOM_BomCountPlural', '{0} BOMs', bomCount);
            right.appendChild(el('span', 'MPC-bom-row-value', bomWord));
            rowEl.appendChild(right);
            return rowEl;
        }

        function buildVerifyRow(row) {
            var rowEl = el('div', 'MPC-bom-row');
            var left = el('div', 'MPC-bom-row-left');
            var bomCode = row.bomCode && String(row.bomCode).trim() ? row.bomCode : '—';
            left.appendChild(el('div', 'MPC-bom-row-primary', bomCode + ' · ' + (row.assemblyName || '')));

            var revision = row.revision && String(row.revision).trim() ? row.revision : '—';
            var updatedByName = row.updatedByName && String(row.updatedByName).trim()
                ? row.updatedByName
                : lbl('VAS_134_BOM_UnknownUser', 'Unknown user');
            var metaText = format('VAS_134_BOM_VerifyMeta', 'Rev {0} · {1} components · updated by {2} · updated {3}',
                revision, row.componentCount || 0, updatedByName, formatRelativeTime(row.updatedAt));
            left.appendChild(el('div', 'MPC-bom-row-meta', metaText));
            rowEl.appendChild(left);

            var right = el('div', 'MPC-bom-row-right');
            var pillClass = row.isVerified ? 'MPC-bom-pill MPC-bom-pill-success' : 'MPC-bom-pill MPC-bom-pill-warn';
            var pillText = row.isVerified ? lbl('VAS_134_BOM_Verified', 'Verified') : lbl('VAS_134_BOM_NotVerified', 'Not verified');
            right.appendChild(el('span', pillClass, pillText));
            rowEl.appendChild(right);
            return rowEl;
        }

        function openModal(key, triggerEl) {
            lastFocusedEl = triggerEl || null;
            var st = modalState[key];
            st.offset = 0;

            var refs = els[key];
            var cfg = MODALS[key];
            refs.intro.textContent = cfg.introText;

            refs.overlay.classList.add('MPC-bom-open');
            refs.overlay.setAttribute('aria-hidden', 'false');
            document.body.classList.add('MPC-bom-body-lock');

            showListMessage(key, lbl('Loading', 'Loading...'));
            loadPage(key, 0);

            refs.close.focus();
        }

        function closeModal(key) {
            var refs = els[key];
            if (!refs || !refs.overlay) { return; }
            refs.overlay.classList.remove('MPC-bom-open');
            refs.overlay.setAttribute('aria-hidden', 'true');
            document.body.classList.remove('MPC-bom-body-lock');
            if (lastFocusedEl && lastFocusedEl.focus) { lastFocusedEl.focus(); }
            lastFocusedEl = null;
        }

        function loadPage(key, offset) {
            var st = modalState[key];
            var cfg = MODALS[key];

            if (st.controller && typeof st.controller.abort === 'function') {
                try { st.controller.abort(); } catch (ignored) { }
            }
            st.controller = (typeof AbortController !== 'undefined') ? new AbortController() : null;
            var myToken = ++st.token;

            var url = VIS.Application.contextUrl + 'VAS_134_BillOfMaterialsWidget/' + cfg.endpoint + '?offset=' + encodeURIComponent(offset) + '&pageSize=' + encodeURIComponent(PAGE_SIZE);
            fetch(url, {
                method: 'GET',
                credentials: 'same-origin',
                signal: st.controller ? st.controller.signal : undefined
            }).then(function (res) { return res.text(); }).then(function (text) {
                if (myToken !== st.token) { return; } // stale response
                var data = parseResponse(text);
                if (!data || data.error) { showListError(key, offset); return; }
                st.offset = data.offset || 0;
                st.total = data.total || 0;
                st.rows = data.rows || [];
                if (key === 'verify') { st.notVerifiedCount = data.notVerifiedCount || 0; }
                renderList(key);
            }).catch(function (err) {
                if (myToken !== st.token) { return; }
                if (err && err.name === 'AbortError') { return; }
                showListError(key, offset);
            });
        }

        function showListMessage(key, message) {
            var refs = els[key];
            refs.list.innerHTML = '';
            refs.list.appendChild(el('div', 'MPC-bom-state', message));
            refs.summary.textContent = '';
            refs.helper.textContent = '';
            refs.pageText.textContent = '1 ' + lbl('VAS_Of', 'of') + ' 1';
            refs.prev.disabled = true;
            refs.next.disabled = true;
        }

        function showListError(key, offset) {
            var refs = els[key];
            refs.list.innerHTML = '';
            var wrap = el('div', 'MPC-bom-state');
            wrap.appendChild(el('span', null, lbl('VAS_CouldntLoad', "Couldn't load")));
            var retry = el('button', 'MPC-bom-retry', lbl('VAS_134_BOM_Retry', 'Retry'));
            retry.type = 'button';
            retry.addEventListener('click', function () { showListMessage(key, lbl('Loading', 'Loading...')); loadPage(key, offset); });
            wrap.appendChild(retry);
            refs.list.appendChild(wrap);
            refs.summary.textContent = '';
            refs.helper.textContent = '';
            refs.pageText.textContent = '1 ' + lbl('VAS_Of', 'of') + ' 1';
            refs.prev.disabled = true;
            refs.next.disabled = true;
        }

        function pageCount(st) {
            return Math.max(1, Math.ceil(st.total / PAGE_SIZE));
        }

        function renderList(key) {
            var refs = els[key];
            var cfg = MODALS[key];
            var st = modalState[key];
            var total = st.total;

            if (!total) {
                refs.list.innerHTML = '';
                refs.list.appendChild(el('div', 'MPC-bom-state', cfg.emptyText));
                refs.summary.textContent = cfg.summaryText(st);
                refs.helper.textContent = '';
                refs.pageText.textContent = '1 ' + lbl('VAS_Of', 'of') + ' 1';
                refs.prev.disabled = true;
                refs.next.disabled = true;
                return;
            }

            refs.list.innerHTML = '';
            st.rows.forEach(function (row) { refs.list.appendChild(cfg.buildRow(row)); });

            var pages = pageCount(st);
            var currentPage = Math.floor(st.offset / PAGE_SIZE) + 1;
            var from = st.offset + 1;
            var to = Math.min(st.offset + st.rows.length, total);

            refs.summary.textContent = cfg.summaryText(st);
            refs.helper.textContent = lbl('VAS_Showing', 'Showing') + ' ' + from + '–' + to + ' ' + lbl('VAS_Of', 'of') + ' ' + total;
            refs.pageText.textContent = currentPage + ' ' + lbl('VAS_Of', 'of') + ' ' + pages;
            refs.prev.disabled = st.offset <= 0;
            refs.next.disabled = (st.offset + PAGE_SIZE) >= total;
        }

        /* ---- Lifecycle ---- */
        this.Initalize = function () {
            build();
            loadSummary();
        };

        this.refreshWidget = function () {
            closeModal('products');
            closeModal('verify');
            loadSummary();
        };

        this.getRoot = function () { return root; };

        this.disposeComponent = function () {
            if (summaryController && typeof summaryController.abort === 'function') {
                try { summaryController.abort(); } catch (ignored) { }
            }
            Object.keys(modalState).forEach(function (key) {
                var st = modalState[key];
                if (st.controller && typeof st.controller.abort === 'function') {
                    try { st.controller.abort(); } catch (ignored) { }
                }
                var refs = els[key];
                if (refs && refs.overlay && refs.overlay.parentNode) { refs.overlay.parentNode.removeChild(refs.overlay); }
            });
            document.body.classList.remove('MPC-bom-body-lock');
            if (root.parentNode) { root.parentNode.removeChild(root); }
        };
    };

    VAS.VAS_134_BillOfMaterialsWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_134_BillOfMaterialsWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_134_BillOfMaterialsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_134_BillOfMaterialsWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_134_BillOfMaterialsWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_134_BillOfMaterialsWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
