/**
 * Top Customers Widget (Product Master dashboard)
 * Widget number 131.
 * Widget size: 2 columns x 3 rows.
 * Ranks customers by distinct products delivered through completed customer
 * shipments (M_InOut MovementType 'C-') in a selected fiscal year, as a bar
 * list; clicking a customer opens a drill-down modal listing every product
 * delivered to that customer (code, attribute chips, delivered qty + base
 * UOM). Fit-to-height widget pagination and dynamic no-scroll modal
 * pagination (both clamp 1-8). Self-contained file - no code shared with any
 * other widget file. Read-only. Plain DOM internals (no jQuery in the widget
 * logic); jQuery is used only at the framework boundary.
 * Backend - VAS_131_TopCustomersWidget/GetFiscalYears, GetRankedPartners,
 *           GetPartnerProducts
 * Summary Message Table
 *  # | Current Text                              | Message Key
 * ---+--------------------------------------------+------------------------
 *  1 | Top Customers                              | VAS_131_TC_Title
 *  2 | Fiscal year                                | VAS_131_TC_FyAria
 *  3 | Last delivery                              | VAS_131_TC_DateMeta
 *  4 | No delivered products in this fiscal year. | VAS_131_TC_EmptyState
 *  5 | product / code / attributes / qty (UoM)    | VAS_131_TC_ColProduct / VAS_131_TC_ColCode / VAS_131_TC_ColAttributes / VAS_131_TC_ColQty
 *  6 | products (count/year/date assembled in code) | VAS_131_TC_ModalSub
 *  7 | Retry                                      | VAS_131_TC_Retry
 *  8 | Showing / of                               | VAS_Showing / VAS_Of (shared global keys)
 *  9 | Couldn't load / Close                      | VAS_CouldntLoad / Close (shared global keys)
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

    var BAR_COLORS = ['#82BEE4', '#7ED8BC', '#EEC981', '#BFAFEA', '#EFAFAF', '#8FD3E8'];
    var MAX_PER_PAGE = 8;
    var ROUTE_BASE = 'VAS_131_TopCustomersWidget';
    // Lucide "users" glyph.
    var ICON_PATHS = '<path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M22 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/>';

    VAS.VAS_131_TopCustomersWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var root = document.createElement('div');
        root.className = 'MPC-cst-root';

        var state = {
            years: [],
            yearId: 0,
            partners: [],
            page: 1,
            perPage: MAX_PER_PAGE,
            firstIndex: 0,
            loaded: false,
            modalPartnerId: null,
            modalItems: [],
            modalMeta: null,
            modalPage: 1,
            modalPerPage: MAX_PER_PAGE,
            modalFirstIndex: 0
        };

        var els = {};
        var yearsController = null;
        var rankedController = null;
        var detailController = null;
        var detailToken = 0;
        var resizeObserver = null;
        var resizeTimer = null;
        var modalResizeHandler = null;
        var modalFitRaf = null;
        var lastFocusedEl = null;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function el(tag, className, text) {
            var node = document.createElement(tag);
            if (className) { node.className = className; }
            if (text != null) { node.textContent = text; }
            return node;
        }

        function svg(paths, extraClass) {
            var s = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' + paths + '</svg>';
            var span = document.createElement('span');
            if (extraClass) { span.className = extraClass; }
            span.innerHTML = s; // static, developer-authored SVG only
            return span.firstChild;
        }

        function formatDate(value) {
            if (!value) { return ''; }
            var iso = String(value).replace(' ', 'T');
            var date = new Date(iso);
            if (isNaN(date.getTime())) { return String(value); }
            return date.toLocaleDateString(window.navigator.language, { day: '2-digit', month: 'short' });
        }

        /* ---- Build the static DOM once ---- */
        function build() {
            var head = el('div', 'MPC-cst-head');
            var headLeft = el('div', 'MPC-cst-head-left');
            var icon = el('span', 'MPC-cst-ico');
            icon.appendChild(svg(ICON_PATHS));
            var titles = el('div', 'MPC-cst-titles');
            titles.appendChild(el('div', 'MPC-cst-title', lbl('VAS_131_TC_Title', 'Top Customers')));
            headLeft.appendChild(icon);
            headLeft.appendChild(titles);

            var fySelect = document.createElement('select');
            fySelect.className = 'MPC-cst-fy';
            fySelect.setAttribute('aria-label', lbl('VAS_131_TC_FyAria', 'Fiscal year'));
            fySelect.addEventListener('change', function () {
                var yearId = parseInt(fySelect.value, 10) || 0;
                if (yearId) { loadRanked(yearId); }
            });

            head.appendChild(headLeft);
            head.appendChild(fySelect);
            els.fySelect = fySelect;

            var rowsClip = el('div', 'MPC-cst-rows');
            var foot = el('div', 'MPC-cst-foot');
            var pager = el('span', 'MPC-cst-pager');
            var prev = el('button', 'MPC-cst-pgbtn');
            prev.type = 'button';
            prev.setAttribute('aria-label', lbl('VAS_PreviousPage', 'Previous page'));
            prev.appendChild(svg('<path d="m15 18-6-6 6-6"/>'));
            var pageText = el('span', 'MPC-cst-pgtext', '1 ' + lbl('VAS_Of', 'of') + ' 1');
            var next = el('button', 'MPC-cst-pgbtn');
            next.type = 'button';
            next.setAttribute('aria-label', lbl('VAS_NextPage', 'Next page'));
            next.appendChild(svg('<path d="m9 18 6-6-6-6"/>'));
            prev.addEventListener('click', function () { if (state.page > 1) { state.page--; renderPage(); } });
            next.addEventListener('click', function () {
                var pages = pageCount();
                if (state.page < pages) { state.page++; renderPage(); }
            });
            pager.appendChild(prev);
            pager.appendChild(pageText);
            pager.appendChild(next);
            foot.appendChild(pager); // right-aligned pager only, no helper text
            els.rows = rowsClip;
            els.pageText = pageText;
            els.prev = prev;
            els.next = next;

            var card = el('div', 'MPC-cst-card');
            card.appendChild(head);
            card.appendChild(rowsClip);
            card.appendChild(foot);
            root.appendChild(card);

            buildModal();
        }

        function buildModal() {
            var overlay = el('div', 'MPC-cst-overlay');
            overlay.setAttribute('aria-hidden', 'true');
            var modal = el('div', 'MPC-cst-modal');
            modal.setAttribute('role', 'dialog');
            modal.setAttribute('aria-modal', 'true');

            var mHead = el('div', 'MPC-cst-m-head');
            var mHeadText = el('div', 'MPC-cst-m-headtext');
            var mTitle = el('div', 'MPC-cst-m-title');
            var mSub = el('div', 'MPC-cst-m-sub');
            mHeadText.appendChild(mTitle);
            mHeadText.appendChild(mSub);
            var mClose = el('button', 'MPC-cst-m-close');
            mClose.type = 'button';
            mClose.setAttribute('aria-label', lbl('Close', 'Close'));
            mClose.appendChild(svg('<path d="M18 6 6 18"/><path d="m6 6 12 12"/>'));
            mClose.addEventListener('click', closeModal);
            mHead.appendChild(mHeadText);
            mHead.appendChild(mClose);

            var mBody = el('div', 'MPC-cst-m-body');

            var mFoot = el('div', 'MPC-cst-m-foot');
            var mHelper = el('span', 'MPC-cst-helper');
            var pager = el('span', 'MPC-cst-pager');
            var prev = el('button', 'MPC-cst-pgbtn');
            prev.type = 'button';
            prev.setAttribute('aria-label', lbl('VAS_PreviousPage', 'Previous page'));
            prev.appendChild(svg('<path d="m15 18-6-6 6-6"/>'));
            var pageText = el('span', 'MPC-cst-pgtext', '1 ' + lbl('VAS_Of', 'of') + ' 1');
            var next = el('button', 'MPC-cst-pgbtn');
            next.type = 'button';
            next.setAttribute('aria-label', lbl('VAS_NextPage', 'Next page'));
            next.appendChild(svg('<path d="m9 18 6-6-6-6"/>'));
            prev.addEventListener('click', function () { if (state.modalPage > 1) { state.modalPage--; renderModalPage(); } });
            next.addEventListener('click', function () {
                var pages = modalPageCount();
                if (state.modalPage < pages) { state.modalPage++; renderModalPage(); }
            });
            pager.appendChild(prev);
            pager.appendChild(pageText);
            pager.appendChild(next);
            mFoot.appendChild(mHelper);
            mFoot.appendChild(pager);

            modal.appendChild(mHead);
            modal.appendChild(mBody);
            modal.appendChild(mFoot);
            overlay.appendChild(modal);

            overlay.addEventListener('mousedown', function (e) { if (e.target === overlay) { closeModal(); } });
            overlay.addEventListener('keydown', function (e) {
                if (e.key === 'Escape') { closeModal(); return; }
                if (e.key === 'Tab') {
                    var focusables = modal.querySelectorAll('button');
                    if (!focusables.length) { return; }
                    var first = focusables[0], last = focusables[focusables.length - 1];
                    if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
                    else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
                }
            });

            document.body.appendChild(overlay);
            els.overlay = overlay;
            els.modal = modal;
            els.mTitle = mTitle;
            els.mSub = mSub;
            els.mBody = mBody;
            els.mClose = mClose;
            els.mHelper = mHelper;
            els.mPageText = pageText;
            els.mPrev = prev;
            els.mNext = next;
        }

        /* ---- Data load ---- */
        function loadYears() {
            if (yearsController && typeof yearsController.abort === 'function') {
                try { yearsController.abort(); } catch (ignored) { }
            }
            yearsController = (typeof AbortController !== 'undefined') ? new AbortController() : null;

            showRowsMessage(lbl('Loading', 'Loading...'));

            var url = VIS.Application.contextUrl + ROUTE_BASE + '/GetFiscalYears';
            fetch(url, {
                method: 'GET',
                credentials: 'same-origin',
                signal: yearsController ? yearsController.signal : undefined
            }).then(function (res) { return res.text(); }).then(function (text) {
                var data = parseResponse(text);
                if (!data || data.error) { showRowsError(); return; }
                state.years = data.years || [];
                renderFySelect();
                var yearId = data.currentYearId || (state.years.length ? state.years[0].yearId : 0);
                if (yearId) { loadRanked(yearId); } else { showEmptyState(); }
            }).catch(function (err) {
                if (err && err.name === 'AbortError') { return; }
                showRowsError();
            });
        }

        function renderFySelect() {
            els.fySelect.innerHTML = '';
            state.years.forEach(function (y) {
                var opt = document.createElement('option');
                opt.value = String(y.yearId);
                opt.textContent = y.label;
                els.fySelect.appendChild(opt);
            });
        }

        function loadRanked(yearId) {
            if (rankedController && typeof rankedController.abort === 'function') {
                try { rankedController.abort(); } catch (ignored) { }
            }
            rankedController = (typeof AbortController !== 'undefined') ? new AbortController() : null;

            state.yearId = yearId;
            els.fySelect.value = String(yearId);
            showRowsMessage(lbl('Loading', 'Loading...'));

            var url = VIS.Application.contextUrl + ROUTE_BASE + '/GetRankedPartners?yearId=' + encodeURIComponent(yearId);
            fetch(url, {
                method: 'GET',
                credentials: 'same-origin',
                signal: rankedController ? rankedController.signal : undefined
            }).then(function (res) { return res.text(); }).then(function (text) {
                var data = parseResponse(text);
                if (!data || data.error) { showRowsError(); return; }
                state.partners = data.partners || [];
                state.loaded = true;
                state.page = 1;
                state.firstIndex = 0;
                renderList(0);
            }).catch(function (err) {
                if (err && err.name === 'AbortError') { return; }
                showRowsError();
            });
        }

        function parseResponse(text) {
            var data = text;
            try {
                if (typeof data === 'string' && data.length) { data = JSON.parse(data); }
                if (typeof data === 'string' && data.length) { data = JSON.parse(data); }
            } catch (e) { return null; }
            return data || {};
        }

        /* ---- List rendering + fit-to-height ---- */
        function pageCount() {
            return Math.max(1, Math.ceil(state.partners.length / state.perPage));
        }

        function maxCount() {
            var max = 0;
            state.partners.forEach(function (p) { if (p.productCount > max) { max = p.productCount; } });
            return max || 1;
        }

        function measurePerPage() {
            var region = els.rows;
            if (!region || !state.partners.length) { return state.perPage; }
            region.innerHTML = '';
            var probe = buildRow(state.partners[0], 0);
            region.appendChild(probe);
            var rowH = probe.offsetHeight || 1;
            var regionH = region.clientHeight || 1;
            region.innerHTML = '';
            return Math.max(1, Math.min(MAX_PER_PAGE, Math.floor(regionH / rowH)));
        }

        function renderList(keepIndex) {
            if (!state.partners.length) {
                state.perPage = MAX_PER_PAGE;
                showEmptyState();
                return;
            }
            state.perPage = measurePerPage();
            var pages = pageCount();
            state.page = Math.min(Math.max(1, Math.floor((keepIndex || 0) / state.perPage) + 1), pages);
            renderPage();
        }

        function renderPage() {
            var region = els.rows;
            region.innerHTML = '';
            var total = state.partners.length;

            if (!total) { showEmptyState(); return; }

            var pages = pageCount();
            if (state.page > pages) { state.page = pages; }
            if (state.page < 1) { state.page = 1; }
            state.firstIndex = (state.page - 1) * state.perPage;
            var slice = state.partners.slice(state.firstIndex, state.firstIndex + state.perPage);

            var max = maxCount();
            slice.forEach(function (p, i) { region.appendChild(buildRow(p, state.firstIndex + i, max)); });

            els.pageText.textContent = state.page + ' ' + lbl('VAS_Of', 'of') + ' ' + pages;
            els.prev.disabled = state.page <= 1;
            els.next.disabled = state.page >= pages;
        }

        function buildRow(partner, absoluteIndex, max) {
            max = max || 1;
            var row = el('button', 'MPC-cst-row');
            row.type = 'button';

            var line1 = el('span', 'MPC-cst-line1');
            var rank = el('span', 'MPC-cst-rank', String(absoluteIndex + 1).padStart(2, '0'));
            var name = el('span', 'MPC-cst-name', partner.name || '');
            var count = el('span', 'MPC-cst-count', String(partner.productCount || 0));
            line1.appendChild(rank);
            line1.appendChild(name);
            line1.appendChild(count);

            var line2 = el('span', 'MPC-cst-line2');
            var barWrap = el('span', 'MPC-cst-barwrap');
            var barTrack = el('span', 'MPC-cst-bartrack');
            var barFill = el('span', 'MPC-cst-barfill');
            var pct = Math.max(6, Math.round((partner.productCount / max) * 100));
            barFill.style.width = pct + '%';
            barFill.style.background = BAR_COLORS[absoluteIndex % BAR_COLORS.length];
            barTrack.appendChild(barFill);
            barWrap.appendChild(barTrack);
            var meta = el('span', 'MPC-cst-meta', lbl('VAS_131_TC_DateMeta', 'Last delivery') + ' ' + formatDate(partner.lastDate));
            line2.appendChild(barWrap);
            line2.appendChild(meta);

            row.appendChild(line1);
            row.appendChild(line2);

            row.addEventListener('click', function () { openModal(partner, row); });
            return row;
        }

        function showRowsMessage(message) {
            els.rows.innerHTML = '';
            els.rows.appendChild(el('div', 'MPC-cst-state', message));
            els.pageText.textContent = '1 ' + lbl('VAS_Of', 'of') + ' 1';
            els.prev.disabled = true;
            els.next.disabled = true;
        }

        function showEmptyState() {
            els.rows.innerHTML = '';
            els.rows.appendChild(el('div', 'MPC-cst-state', lbl('VAS_131_TC_EmptyState', 'No delivered products in this fiscal year.')));
            els.pageText.textContent = '1 ' + lbl('VAS_Of', 'of') + ' 1';
            els.prev.disabled = true;
            els.next.disabled = true;
        }

        function showRowsError() {
            els.rows.innerHTML = '';
            var wrap = el('div', 'MPC-cst-state');
            wrap.appendChild(el('span', null, lbl('VAS_CouldntLoad', "Couldn't load")));
            var retry = el('button', 'MPC-cst-retry', lbl('VAS_131_TC_Retry', 'Retry'));
            retry.type = 'button';
            retry.addEventListener('click', function () {
                if (state.yearId) { loadRanked(state.yearId); } else { loadYears(); }
            });
            wrap.appendChild(retry);
            els.rows.appendChild(wrap);
            els.pageText.textContent = '1 ' + lbl('VAS_Of', 'of') + ' 1';
            els.prev.disabled = true;
            els.next.disabled = true;
        }

        /* ---- Modal ---- */
        function openModal(partner, rowEl) {
            state.modalPartnerId = partner.partnerId;
            state.modalItems = [];
            state.modalMeta = null;
            state.modalPage = 1;
            state.modalFirstIndex = 0;
            lastFocusedEl = rowEl || null;

            els.mTitle.textContent = partner.name || '';
            els.mSub.textContent = '';

            els.overlay.classList.add('MPC-cst-open');
            els.overlay.setAttribute('aria-hidden', 'false');
            document.body.classList.add('MPC-cst-body-lock');

            showModalMessage(lbl('Loading', 'Loading...'));
            loadPartnerProducts(partner.partnerId);

            attachModalResizeHandler();
            els.mClose.focus();
        }

        function closeModal() {
            if (!els.overlay) { return; }
            els.overlay.classList.remove('MPC-cst-open');
            els.overlay.setAttribute('aria-hidden', 'true');
            document.body.classList.remove('MPC-cst-body-lock');
            detachModalResizeHandler();
            state.modalPartnerId = null;
            if (lastFocusedEl && lastFocusedEl.focus) { lastFocusedEl.focus(); }
            lastFocusedEl = null;
        }

        function loadPartnerProducts(partnerId) {
            if (detailController && typeof detailController.abort === 'function') {
                try { detailController.abort(); } catch (ignored) { }
            }
            detailController = (typeof AbortController !== 'undefined') ? new AbortController() : null;
            var myToken = ++detailToken;

            var url = VIS.Application.contextUrl + ROUTE_BASE + '/GetPartnerProducts?partnerId=' + encodeURIComponent(partnerId) + '&yearId=' + encodeURIComponent(state.yearId);
            fetch(url, {
                method: 'GET',
                credentials: 'same-origin',
                signal: detailController ? detailController.signal : undefined
            }).then(function (res) { return res.text(); }).then(function (text) {
                if (myToken !== detailToken) { return; } // stale response - user switched customer
                var data = parseResponse(text);
                if (!data || data.error) { showModalError(partnerId); return; }
                state.modalItems = data.items || [];
                state.modalMeta = data;
                var yearLabel = (state.years.filter(function (y) { return y.yearId === state.yearId; })[0] || {}).label || data.label || '';
                els.mSub.textContent = state.modalItems.length + ' ' + lbl('VAS_131_TC_ModalSub', 'products') + ' · ' + yearLabel +
                    ' · ' + lbl('VAS_131_TC_DateMeta', 'Last delivery') + ' ' + formatDate(data.lastDate);
                sizeAndPaginateModal(0);
            }).catch(function (err) {
                if (myToken !== detailToken) { return; }
                if (err && err.name === 'AbortError') { return; }
                showModalError(partnerId);
            });
        }

        function showModalMessage(message) {
            els.mBody.innerHTML = '';
            els.mBody.appendChild(el('div', 'MPC-cst-state', message));
            els.mHelper.textContent = '';
            els.mPageText.textContent = '1 ' + lbl('VAS_Of', 'of') + ' 1';
            els.mPrev.disabled = true;
            els.mNext.disabled = true;
        }

        function showModalError(partnerId) {
            els.mBody.innerHTML = '';
            var wrap = el('div', 'MPC-cst-state');
            wrap.appendChild(el('span', null, lbl('VAS_CouldntLoad', "Couldn't load")));
            var retry = el('button', 'MPC-cst-retry', lbl('VAS_131_TC_Retry', 'Retry'));
            retry.type = 'button';
            retry.addEventListener('click', function () { showModalMessage(lbl('Loading', 'Loading...')); loadPartnerProducts(partnerId); });
            wrap.appendChild(retry);
            els.mBody.appendChild(wrap);
            els.mHelper.textContent = '';
            els.mPageText.textContent = '1 ' + lbl('VAS_Of', 'of') + ' 1';
            els.mPrev.disabled = true;
            els.mNext.disabled = true;
        }

        function buildModalHeadRow() {
            var head = el('div', 'MPC-cst-m-row MPC-cst-m-ghead');
            head.appendChild(el('span', null, lbl('VAS_131_TC_ColProduct', 'product')));
            head.appendChild(el('span', null, lbl('VAS_131_TC_ColCode', 'code')));
            head.appendChild(el('span', null, lbl('VAS_131_TC_ColAttributes', 'attributes')));
            head.appendChild(el('span', null, lbl('VAS_131_TC_ColQty', 'qty (UoM)')));
            return head;
        }

        function buildModalDataRow(item) {
            var row = el('div', 'MPC-cst-m-row MPC-cst-m-datarow');

            row.appendChild(el('span', 'MPC-cst-m-primary', item.name || ''));
            row.appendChild(el('span', 'MPC-cst-m-cell', item.code || ''));

            var attrsCell = el('span', 'MPC-cst-m-attrs');
            var attrs = item.attributes || [];
            if (attrs.length) {
                attrs.forEach(function (a) {
                    attrsCell.appendChild(el('span', 'MPC-cst-chip', a.label + ': ' + a.value));
                });
            } else {
                attrsCell.appendChild(el('span', 'MPC-cst-em-dash', '—'));
            }
            row.appendChild(attrsCell);

            var qty = (item.qty != null) ? item.qty : 0;
            row.appendChild(el('span', 'MPC-cst-m-qty', qty + ' ' + (item.uom || '')));

            return row;
        }

        function modalPageCount() {
            return Math.max(1, Math.ceil(state.modalItems.length / state.modalPerPage));
        }

        /* Same convergent technique as VAS_097_ExpectedGRNWidget's
           fitModalLines(): the modal has no fixed height, only a max-height
           cap (CSS), so instead of measuring a pre-sized box we measure how
           much MORE room the modal could still grow into before hitting that
           cap ("slack") and compute how many rows would fit if it used all
           of it. If that differs from the current page size, re-render at
           the new size (which grows/shrinks the modal, since it's
           content-driven) and the next measurement converges. */
        function fitModalRows() {
            if (!state.modalItems.length) { return; }
            var head = els.mBody.querySelector('.MPC-cst-m-ghead');
            var row = els.mBody.querySelector('.MPC-cst-m-datarow');
            if (!head || !row) { return; }
            var headH = head.offsetHeight || 1;
            var rowH = row.offsetHeight || 1;
            var slack = Math.max(0, Math.floor(window.innerHeight * 0.88) - Math.ceil(els.modal.getBoundingClientRect().height));
            var fit = Math.max(1, Math.min(MAX_PER_PAGE, Math.floor((els.mBody.clientHeight - headH + slack) / rowH)));

            if (fit !== state.modalPerPage) {
                var firstIndex = state.modalFirstIndex;
                state.modalPerPage = fit;
                state.modalPage = Math.floor(firstIndex / state.modalPerPage) + 1;
                renderModalPage();
            }

            reserveModalHeight(headH, rowH);
        }

        /* Widget-development-rules #15: a modal/widget that shows lines of
           data must not resize with the number of lines on the CURRENT
           page. Reserve the height of a full page (state.modalPerPage rows)
           on the body so a shorter last/partial page never shrinks the
           modal - only a resize/reopen recomputes this. */
        function reserveModalHeight(headH, rowH) {
            var cs = window.getComputedStyle(els.mBody);
            var paddingV = (parseFloat(cs.paddingTop) || 0) + (parseFloat(cs.paddingBottom) || 0);
            els.mBody.style.minHeight = Math.ceil(headH + (state.modalPerPage * rowH) + paddingV) + 'px';
        }

        function sizeAndPaginateModal(keepIndex) {
            if (!state.modalItems.length) {
                els.mBody.innerHTML = '';
                els.mBody.appendChild(buildModalHeadRow());
                els.mBody.appendChild(el('div', 'MPC-cst-state', lbl('VAS_131_TC_EmptyState', 'No delivered products in this fiscal year.')));
                els.mHelper.textContent = '';
                els.mPageText.textContent = '1 ' + lbl('VAS_Of', 'of') + ' 1';
                els.mPrev.disabled = true;
                els.mNext.disabled = true;
                return;
            }

            if (!state.modalPerPage) { state.modalPerPage = MAX_PER_PAGE; }
            var pages = modalPageCount();
            state.modalPage = Math.min(Math.max(1, Math.floor((keepIndex || 0) / state.modalPerPage) + 1), pages);
            renderModalPage();
            window.setTimeout(fitModalRows, 0);
        }

        function renderModalPage() {
            var total = state.modalItems.length;
            var pages = modalPageCount();
            if (state.modalPage > pages) { state.modalPage = pages; }
            if (state.modalPage < 1) { state.modalPage = 1; }
            state.modalFirstIndex = (state.modalPage - 1) * state.modalPerPage;
            var slice = state.modalItems.slice(state.modalFirstIndex, state.modalFirstIndex + state.modalPerPage);

            els.mBody.innerHTML = '';
            els.mBody.appendChild(buildModalHeadRow());
            slice.forEach(function (item) { els.mBody.appendChild(buildModalDataRow(item)); });

            var from = total ? state.modalFirstIndex + 1 : 0;
            var to = state.modalFirstIndex + slice.length;
            els.mHelper.textContent = lbl('VAS_Showing', 'Showing') + ' ' + from + '–' + to + ' ' + lbl('VAS_Of', 'of') + ' ' + total;
            els.mPageText.textContent = state.modalPage + ' ' + lbl('VAS_Of', 'of') + ' ' + pages;
            els.mPrev.disabled = state.modalPage <= 1;
            els.mNext.disabled = state.modalPage >= pages;
        }

        /* The modal never scrolls: whenever its size changes (window resize,
           browser zoom), refit how many rows are shown - same
           ResizeObserver + rAF-debounce technique as
           VAS_097_ExpectedGRNWidget's modalResizeObserver. */
        function attachModalResizeHandler() {
            if (modalResizeHandler || typeof ResizeObserver === 'undefined') { return; }
            modalResizeHandler = new ResizeObserver(function () {
                if (modalFitRaf) { window.cancelAnimationFrame(modalFitRaf); }
                modalFitRaf = window.requestAnimationFrame(fitModalRows);
            });
            modalResizeHandler.observe(els.mBody);
        }

        function detachModalResizeHandler() {
            if (modalResizeHandler) { modalResizeHandler.disconnect(); modalResizeHandler = null; }
            if (modalFitRaf) { window.cancelAnimationFrame(modalFitRaf); modalFitRaf = null; }
        }

        /* ---- Lifecycle ---- */
        this.Initalize = function () {
            build();

            if (typeof ResizeObserver !== 'undefined') {
                resizeObserver = new ResizeObserver(function () {
                    if (resizeTimer) { clearTimeout(resizeTimer); }
                    resizeTimer = setTimeout(function () {
                        if (state.loaded && state.partners.length) {
                            renderList(state.firstIndex);
                        }
                    }, 60);
                });
                resizeObserver.observe(els.rows);
            }

            loadYears();
        };

        this.refreshWidget = function () {
            closeModal();
            loadYears();
        };

        this.getRoot = function () { return root; };

        this.disposeComponent = function () {
            if (yearsController && typeof yearsController.abort === 'function') {
                try { yearsController.abort(); } catch (ignored) { }
            }
            if (rankedController && typeof rankedController.abort === 'function') {
                try { rankedController.abort(); } catch (ignored) { }
            }
            if (detailController && typeof detailController.abort === 'function') {
                try { detailController.abort(); } catch (ignored) { }
            }
            if (resizeObserver) { resizeObserver.disconnect(); resizeObserver = null; }
            if (resizeTimer) { clearTimeout(resizeTimer); }
            detachModalResizeHandler();
            if (els.overlay && els.overlay.parentNode) { els.overlay.parentNode.removeChild(els.overlay); }
            document.body.classList.remove('MPC-cst-body-lock');
            if (root.parentNode) { root.parentNode.removeChild(root); }
        };
    };

    VAS.VAS_131_TopCustomersWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_131_TopCustomersWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_131_TopCustomersWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_131_TopCustomersWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_131_TopCustomersWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_131_TopCustomersWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
