/**
 * Products by Type Widget (Product Master dashboard)
 * Widget number 129.
 * Widget size: 3 columns x 2 rows.
 * Catalog-composition donut split into the four fixed product types (Item /
 * Service / Resource / Expense): count + share per type, with an interactive
 * donut and legend. Clicking a segment or legend row opens a drill-down modal
 * listing every product of that type (code, category, base UOM, attribute
 * chips, active/inactive status) with dynamic no-scroll pagination capped at
 * 8 rows per page. Read-only. Plain DOM internals (no jQuery in the widget
 * logic); jQuery is used only at the framework boundary.
 * Backend - VAS_129_ProductsByTypeWidget/GetTypeCounts
 *           VAS_129_ProductsByTypeWidget/GetProductsByType
 * Summary Message Table
 *  # | Current Text                          | Message Key
 * ---+----------------------------------------+------------------------
 *  1 | Products by Type                      | VAS_129_PBT_Title
 *  2 | Tap a type to view its products       | VAS_129_PBT_Subtitle
 *  3 | Item / Service / Resource / Expense   | VAS_129_PBT_TypeItem / VAS_129_PBT_TypeService / VAS_129_PBT_TypeResource / VAS_129_PBT_TypeExpense
 *  4 | Products                              | VAS_129_PBT_ProductsLabel
 *  5 | products across 4 types (count prepended in code) | VAS_129_PBT_Helper
 *  6 | product / code / category / UoM / attributes / status | VAS_129_PBT_ColProduct / VAS_129_PBT_ColCode / VAS_129_PBT_ColCategory / VAS_129_PBT_ColUom / VAS_129_PBT_ColAttributes / VAS_129_PBT_ColStatus
 *  7 | products (count prepended in code)    | VAS_129_PBT_ModalSub
 *  8 | Active / Inactive                     | VAS_129_PBT_Active / VAS_129_PBT_Inactive
 *  9 | Showing / of                          | VAS_Showing / VAS_Of
 * 10 | No products of this type.             | VAS_129_PBT_EmptyState
 * 11 | Couldn't load / Retry                 | VAS_CouldntLoad / VAS_129_PBT_Retry
 * 12 | Close                                 | Close
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

    VAS.VAS_129_ProductsByTypeWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var root = document.createElement('div');
        root.className = 'MPC-pt-root';

        // Fixed type catalog, donut/legend order. label = readable companion tone.
        var TYPES = [
            { key: 'I', msg: 'VAS_129_PBT_TypeItem', fallback: 'Item', fill: '#82BEE4', label: '#2E6E9E' },
            { key: 'S', msg: 'VAS_129_PBT_TypeService', fallback: 'Service', fill: '#7ED8BC', label: '#1E7A64' },
            { key: 'R', msg: 'VAS_129_PBT_TypeResource', fallback: 'Resource', fill: '#EEC981', label: '#9A6500' },
            { key: 'E', msg: 'VAS_129_PBT_TypeExpense', fallback: 'Expense', fill: '#BFAFEA', label: '#5F4AA6' }
        ];
        var TYPE_BY_KEY = {};
        TYPES.forEach(function (t) { TYPE_BY_KEY[t.key] = t; });

        var MAX_PER_PAGE = 8;

        var state = {
            counts: { I: 0, S: 0, R: 0, E: 0 },
            total: 0,
            loaded: false,
            modalType: null,
            modalItems: [],
            modalPage: 1,
            modalPerPage: MAX_PER_PAGE,
            modalFirstIndex: 0
        };

        var els = {};
        var countsController = null;
        var detailController = null;
        var detailToken = 0;
        var modalResizeObserver = null;
        var modalFitRaf = null;
        var lastFocusedEl = null;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function typeLabel(key) {
            var t = TYPE_BY_KEY[key];
            return t ? lbl(t.msg, t.fallback) : key;
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

        /* ---- Build the static DOM once ---- */
        function build() {
            var head = el('div', 'MPC-pt-head');
            var headLeft = el('div', 'MPC-pt-head-left');
            var icon = el('span', 'MPC-pt-ico');
            icon.appendChild(svg('<path d="m12.83 2.18a2 2 0 0 0-1.66 0L2.6 6.08a1 1 0 0 0 0 1.83l8.58 3.91a2 2 0 0 0 1.66 0l8.58-3.9a1 1 0 0 0 0-1.83Z"/><path d="m22 17.65-9.17 4.16a2 2 0 0 1-1.66 0L2 17.65"/><path d="m22 12.65-9.17 4.16a2 2 0 0 1-1.66 0L2 12.65"/>'));
            var titles = el('div', 'MPC-pt-titles');
            titles.appendChild(el('div', 'MPC-pt-title', lbl('VAS_129_PBT_Title', 'Products by Type')));
            titles.appendChild(el('div', 'MPC-pt-subtitle', lbl('VAS_129_PBT_Subtitle', 'Tap a type to view its products')));
            headLeft.appendChild(icon);
            headLeft.appendChild(titles);
            head.appendChild(headLeft);

            var body = el('div', 'MPC-pt-body');
            var donutWrap = el('div', 'MPC-pt-donut-wrap');
            var donutSvg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
            donutSvg.setAttribute('viewBox', '0 0 120 120');
            donutSvg.setAttribute('class', 'MPC-pt-donut');
            var center = el('div', 'MPC-pt-donut-center');
            var total = el('div', 'MPC-pt-donut-total', '0');
            var totalLbl = el('div', 'MPC-pt-donut-label', lbl('VAS_129_PBT_ProductsLabel', 'Products'));
            center.appendChild(total);
            center.appendChild(totalLbl);
            donutWrap.appendChild(donutSvg);
            donutWrap.appendChild(center);

            var legend = el('div', 'MPC-pt-legend');

            body.appendChild(donutWrap);
            body.appendChild(legend);
            els.body = body;
            els.donutSvg = donutSvg;
            els.donutTotal = total;
            els.legend = legend;

            var helper = el('div', 'MPC-pt-helper');
            els.helper = helper;

            var card = el('div', 'MPC-pt-card');
            card.appendChild(head);
            card.appendChild(body);
            card.appendChild(helper);
            root.appendChild(card);

            buildModal();
        }

        function buildModal() {
            var overlay = el('div', 'MPC-pt-overlay');
            overlay.setAttribute('aria-hidden', 'true');
            var modal = el('div', 'MPC-pt-modal');
            modal.setAttribute('role', 'dialog');
            modal.setAttribute('aria-modal', 'true');

            var mHead = el('div', 'MPC-pt-m-head');
            var mHeadText = el('div', 'MPC-pt-m-headtext');
            var mSwatch = el('span', 'MPC-pt-m-swatch');
            var mTitleWrap = el('div', 'MPC-pt-m-titlewrap');
            var mTitle = el('div', 'MPC-pt-m-title');
            var mSub = el('div', 'MPC-pt-m-sub');
            mTitleWrap.appendChild(mTitle);
            mTitleWrap.appendChild(mSub);
            mHeadText.appendChild(mSwatch);
            mHeadText.appendChild(mTitleWrap);
            var mClose = el('button', 'MPC-pt-m-close');
            mClose.type = 'button';
            mClose.setAttribute('aria-label', lbl('Close', 'Close'));
            mClose.appendChild(svg('<path d="M18 6 6 18"/><path d="m6 6 12 12"/>'));
            mClose.addEventListener('click', closeModal);
            mHead.appendChild(mHeadText);
            mHead.appendChild(mClose);

            var mBody = el('div', 'MPC-pt-m-body');

            var mFoot = el('div', 'MPC-pt-m-foot');
            var mHelper = el('span', 'MPC-pt-helper');
            var pager = el('span', 'MPC-pt-pager');
            var prev = el('button', 'MPC-pt-pgbtn');
            prev.type = 'button';
            prev.setAttribute('aria-label', lbl('VAS_PreviousPage', 'Previous page'));
            prev.appendChild(svg('<path d="m15 18-6-6 6-6"/>'));
            var pageText = el('span', 'MPC-pt-pgtext', '1 ' + lbl('VAS_Of', 'of') + ' 1');
            var next = el('button', 'MPC-pt-pgbtn');
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
            els.mSwatch = mSwatch;
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
        function loadCounts() {
            if (countsController && typeof countsController.abort === 'function') {
                try { countsController.abort(); } catch (ignored) { }
            }
            countsController = (typeof AbortController !== 'undefined') ? new AbortController() : null;

            showBodyMessage(lbl('Loading', 'Loading...'));

            var url = VIS.Application.contextUrl + 'VAS_129_ProductsByTypeWidget/GetTypeCounts';
            fetch(url, {
                method: 'GET',
                credentials: 'same-origin',
                signal: countsController ? countsController.signal : undefined
            }).then(function (res) { return res.text(); }).then(function (text) {
                var data = parseResponse(text);
                if (!data || data.error) { showBodyError(); return; }
                state.counts = data.counts || { I: 0, S: 0, R: 0, E: 0 };
                state.total = data.total || 0;
                state.loaded = true;
                renderDonutAndLegend();
            }).catch(function (err) {
                if (err && err.name === 'AbortError') { return; }
                showBodyError();
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

        function showBodyMessage(message) {
            els.donutSvg.innerHTML = '';
            els.donutTotal.textContent = '';
            els.legend.innerHTML = '';
            els.legend.appendChild(el('div', 'MPC-pt-state', message));
            els.helper.textContent = '';
        }

        function showBodyError() {
            els.donutSvg.innerHTML = '';
            els.donutTotal.textContent = '';
            els.legend.innerHTML = '';
            var wrap = el('div', 'MPC-pt-state');
            wrap.appendChild(el('span', null, lbl('VAS_CouldntLoad', "Couldn't load")));
            var retry = el('button', 'MPC-pt-retry', lbl('VAS_129_PBT_Retry', 'Retry'));
            retry.type = 'button';
            retry.addEventListener('click', loadCounts);
            wrap.appendChild(retry);
            els.legend.appendChild(wrap);
            els.helper.textContent = '';
        }

        /* ---- Donut + legend ---- */
        function renderDonutAndLegend() {
            var R = 44, CX = 60, CY = 60, C = 2 * Math.PI * R, STROKE = 17;
            var total = state.total;

            els.donutTotal.textContent = total;
            els.helper.textContent = total + ' ' + lbl('VAS_129_PBT_Helper', 'products across 4 types');

            var svgHtml = '<circle cx="' + CX + '" cy="' + CY + '" r="' + R + '" fill="none" stroke="#EDF2F6" stroke-width="' + STROKE + '"/>';
            var offset = 0;
            TYPES.forEach(function (t) {
                var count = state.counts[t.key] || 0;
                var frac = total > 0 ? count / total : 0;
                var seg = frac > 0 ? Math.max(frac * C - 2, 0.5) : 0;
                if (seg > 0) {
                    svgHtml += '<circle class="MPC-pt-donut-seg" tabindex="0" role="button" data-t="' + t.key + '"' +
                        ' aria-label="' + typeLabel(t.key) + '"' +
                        ' cx="' + CX + '" cy="' + CY + '" r="' + R + '"' +
                        ' fill="none" stroke="' + t.fill + '" stroke-width="' + STROKE + '" stroke-linecap="butt"' +
                        ' stroke-dasharray="' + seg + ' ' + (C - seg) + '"' +
                        ' stroke-dashoffset="' + (-offset) + '"' +
                        ' transform="rotate(-90 ' + CX + ' ' + CY + ')"/>';
                }
                offset += frac * C;
            });
            els.donutSvg.innerHTML = svgHtml;

            var segs = els.donutSvg.querySelectorAll('.MPC-pt-donut-seg');
            Array.prototype.forEach.call(segs, function (seg) {
                seg.addEventListener('click', function () { openModal(seg.dataset.t, seg); });
                seg.addEventListener('keydown', function (e) {
                    if (e.key === 'Enter' || e.key === ' ' || e.key === 'Spacebar') {
                        e.preventDefault();
                        openModal(seg.dataset.t, seg);
                    }
                });
                seg.addEventListener('mouseenter', function () { seg.setAttribute('stroke-width', String(STROKE + 3)); });
                seg.addEventListener('mouseleave', function () { seg.setAttribute('stroke-width', String(STROKE)); });
            });

            els.legend.innerHTML = '';
            TYPES.forEach(function (t) {
                var count = state.counts[t.key] || 0;
                var pct = total > 0 ? Math.round((count / total) * 100) : 0;

                var row = el('button', 'MPC-pt-legend-row');
                row.type = 'button';

                var left = el('span', 'MPC-pt-legend-left');
                var swatch = el('span', 'MPC-pt-swatch');
                swatch.style.background = t.fill;
                left.appendChild(swatch);
                left.appendChild(el('span', 'MPC-pt-legend-name', typeLabel(t.key)));

                var right = el('span', 'MPC-pt-legend-right');
                var pctSpan = el('span', 'MPC-pt-legend-pct', pct + '%');
                pctSpan.style.color = t.label;
                right.appendChild(pctSpan);
                right.appendChild(el('span', 'MPC-pt-legend-count', String(count)));

                row.appendChild(left);
                row.appendChild(right);
                row.addEventListener('click', function () { openModal(t.key, row); });
                els.legend.appendChild(row);
            });
        }

        /* ---- Modal ---- */
        function openModal(typeKey, originEl) {
            state.modalType = typeKey;
            state.modalItems = [];
            state.modalPage = 1;
            state.modalFirstIndex = 0;
            lastFocusedEl = originEl || null;

            var t = TYPE_BY_KEY[typeKey];
            els.mSwatch.style.background = t ? t.fill : '#CCCCCC';
            els.mTitle.textContent = typeLabel(typeKey) + ' ' + lbl('VAS_129_PBT_ProductsWord', 'Products');
            els.mSub.textContent = '';

            els.overlay.classList.add('MPC-pt-open');
            els.overlay.setAttribute('aria-hidden', 'false');
            document.body.classList.add('MPC-pt-body-lock');

            showModalMessage(lbl('Loading', 'Loading...'));
            loadTypeProducts(typeKey);

            attachResizeHandler();
            els.mClose.focus();
        }

        function closeModal() {
            if (!els.overlay) { return; }
            els.overlay.classList.remove('MPC-pt-open');
            els.overlay.setAttribute('aria-hidden', 'true');
            document.body.classList.remove('MPC-pt-body-lock');
            detachResizeHandler();
            state.modalType = null;
            if (lastFocusedEl && lastFocusedEl.focus) { lastFocusedEl.focus(); }
            lastFocusedEl = null;
        }

        function loadTypeProducts(typeKey) {
            if (detailController && typeof detailController.abort === 'function') {
                try { detailController.abort(); } catch (ignored) { }
            }
            detailController = (typeof AbortController !== 'undefined') ? new AbortController() : null;
            var myToken = ++detailToken;

            var url = VIS.Application.contextUrl + 'VAS_129_ProductsByTypeWidget/GetProductsByType?productType=' + encodeURIComponent(typeKey);
            fetch(url, {
                method: 'GET',
                credentials: 'same-origin',
                signal: detailController ? detailController.signal : undefined
            }).then(function (res) { return res.text(); }).then(function (text) {
                if (myToken !== detailToken) { return; } // stale response - user switched type
                var data = parseResponse(text);
                if (!data || data.error) { showModalError(typeKey); return; }
                state.modalItems = data.items || [];
                els.mSub.textContent = state.modalItems.length + ' ' + lbl('VAS_129_PBT_ModalSub', 'products');
                sizeAndPaginateModal(0);
            }).catch(function (err) {
                if (myToken !== detailToken) { return; }
                if (err && err.name === 'AbortError') { return; }
                showModalError(typeKey);
            });
        }

        function showModalMessage(message) {
            els.mBody.innerHTML = '';
            els.mBody.appendChild(el('div', 'MPC-pt-state', message));
            els.mHelper.textContent = '';
            els.mPageText.textContent = '1 ' + lbl('VAS_Of', 'of') + ' 1';
            els.mPrev.disabled = true;
            els.mNext.disabled = true;
        }

        function showModalError(typeKey) {
            els.mBody.innerHTML = '';
            var wrap = el('div', 'MPC-pt-state');
            wrap.appendChild(el('span', null, lbl('VAS_CouldntLoad', "Couldn't load")));
            var retry = el('button', 'MPC-pt-retry', lbl('VAS_129_PBT_Retry', 'Retry'));
            retry.type = 'button';
            retry.addEventListener('click', function () { showModalMessage(lbl('Loading', 'Loading...')); loadTypeProducts(typeKey); });
            wrap.appendChild(retry);
            els.mBody.appendChild(wrap);
            els.mHelper.textContent = '';
            els.mPageText.textContent = '1 ' + lbl('VAS_Of', 'of') + ' 1';
            els.mPrev.disabled = true;
            els.mNext.disabled = true;
        }

        function buildModalHeadRow() {
            var head = el('div', 'MPC-pt-m-row MPC-pt-m-ghead');
            head.appendChild(el('span', null, lbl('VAS_129_PBT_ColProduct', 'product')));
            head.appendChild(el('span', null, lbl('VAS_129_PBT_ColCode', 'code')));
            head.appendChild(el('span', null, lbl('VAS_129_PBT_ColCategory', 'category')));
            var uom = el('span', 'MPC-pt-uom-label', lbl('VAS_129_PBT_ColUom', 'UoM'));
            head.appendChild(uom);
            head.appendChild(el('span', null, lbl('VAS_129_PBT_ColAttributes', 'attributes')));
            head.appendChild(el('span', null, lbl('VAS_129_PBT_ColStatus', 'status')));
            return head;
        }

        function buildModalDataRow(item) {
            var row = el('div', 'MPC-pt-m-row MPC-pt-m-datarow');

            row.appendChild(el('span', 'MPC-pt-m-primary', item.name || ''));
            row.appendChild(el('span', 'MPC-pt-m-cell', item.code || ''));
            row.appendChild(el('span', 'MPC-pt-m-cell', item.category || ''));
            row.appendChild(el('span', 'MPC-pt-m-cell', item.uom || ''));

            var attrsCell = el('span', 'MPC-pt-m-attrs');
            var attrs = item.attributes || [];
            if (attrs.length) {
                attrs.forEach(function (a) {
                    attrsCell.appendChild(el('span', 'MPC-pt-chip', a.label + ': ' + a.value));
                });
            } else {
                attrsCell.appendChild(el('span', 'MPC-pt-em-dash', '—'));
            }
            row.appendChild(attrsCell);

            var statusText = item.isActive ? lbl('VAS_129_PBT_Active', 'Active') : lbl('VAS_129_PBT_Inactive', 'Inactive');
            var statusCell = el('span', 'MPC-pt-status ' + (item.isActive ? 'MPC-pt-status-active' : 'MPC-pt-status-inactive'), statusText);
            row.appendChild(statusCell);

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
            var head = els.mBody.querySelector('.MPC-pt-m-ghead');
            var row = els.mBody.querySelector('.MPC-pt-m-datarow');
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
                els.mBody.appendChild(el('div', 'MPC-pt-state', lbl('VAS_129_PBT_EmptyState', 'No products of this type.')));
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
        function attachResizeHandler() {
            if (modalResizeObserver || typeof ResizeObserver === 'undefined') { return; }
            modalResizeObserver = new ResizeObserver(function () {
                if (modalFitRaf) { window.cancelAnimationFrame(modalFitRaf); }
                modalFitRaf = window.requestAnimationFrame(fitModalRows);
            });
            modalResizeObserver.observe(els.mBody);
        }

        function detachResizeHandler() {
            if (modalResizeObserver) { modalResizeObserver.disconnect(); modalResizeObserver = null; }
            if (modalFitRaf) { window.cancelAnimationFrame(modalFitRaf); modalFitRaf = null; }
        }

        /* ---- Lifecycle ---- */
        this.Initalize = function () {
            build();
            loadCounts();
        };

        this.refreshWidget = function () {
            closeModal();
            loadCounts();
        };

        this.getRoot = function () { return root; };

        this.disposeComponent = function () {
            if (countsController && typeof countsController.abort === 'function') {
                try { countsController.abort(); } catch (ignored) { }
            }
            if (detailController && typeof detailController.abort === 'function') {
                try { detailController.abort(); } catch (ignored) { }
            }
            detachResizeHandler();
            if (els.overlay && els.overlay.parentNode) { els.overlay.parentNode.removeChild(els.overlay); }
            document.body.classList.remove('MPC-pt-body-lock');
            if (root.parentNode) { root.parentNode.removeChild(root); }
        };
    };

    VAS.VAS_129_ProductsByTypeWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_129_ProductsByTypeWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_129_ProductsByTypeWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_129_ProductsByTypeWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_129_ProductsByTypeWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_129_ProductsByTypeWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
