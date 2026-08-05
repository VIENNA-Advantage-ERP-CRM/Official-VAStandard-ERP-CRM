/**
 * Updated This Month Widget (Product Master dashboard)
 * Widget number 133.
 * Widget size: 2 columns x 1 row.
 * KPI tile (a real button) showing how many M_Product/M_ProductPrice records
 * had their own Updated timestamp fall in the current calendar month, plus
 * distinct updater count and today's count. Clicking it opens a drill-down
 * modal (Onfinity Panel Foundation) listing the individual update records,
 * newest first, server-paginated at a fixed 8 rows per page. No JS pixel
 * measurement is used to size the list: row height and the list's max-height
 * are both set in em (see .MPC-utm-row / .MPC-utm-list in the CSS) against
 * the SAME fluid modal font-size, so "8 rows fit without a scrollbar" holds
 * by construction at every screen size - the ratio never changes because
 * both values scale together. This is a LATEST-RECORD update view
 * (M_Product -> "Attribute", M_ProductPrice -> "Price"), not AD_ChangeLog /
 * a field-level audit trail. Read-only. Plain DOM internals (no jQuery in
 * the widget logic); jQuery is used only at the framework boundary.
 * Backend - VAS_133_UpdatedThisMonthWidget/GetSummary, GetPagedUpdates
 * Summary Message Table
 *  # | Current Text                                     | Message Key
 * ---+---------------------------------------------------+------------------------
 *  1 | Updated This Month                               | VAS_133_UTM_Title
 *  2 | Updates by / users / today (count assembled in code) | VAS_133_UTM_Meta / VAS_133_UTM_UsersWord / VAS_133_UTM_TodayWord
 *  3 | Item master changes recorded since / products / users. (dates/counts assembled in code) | VAS_133_UTM_Intro / VAS_133_UTM_ProductsWord / VAS_133_UTM_UsersWord
 *  4 | Recent updates                                   | VAS_133_UTM_SectionTitle
 *  5 | updates (count prepended in code)                | VAS_133_UTM_SectionSummary
 *  6 | Unknown user                                     | VAS_133_UTM_UnknownUser
 *  7 | No product or price records were updated this month. | VAS_133_UTM_EmptyState
 *  8 | Just now / m ago / h ago / day / days / ago      | VAS_133_UTM_JustNow / VAS_133_UTM_MinsAgo / VAS_133_UTM_HoursAgo / VAS_133_UTM_DayWord / VAS_133_UTM_DaysWord / VAS_133_UTM_DaysAgo
 *    | (all numbers prepended in code, e.g. "5" + "m ago") |
 *  9 | Retry                                             | VAS_133_UTM_Retry
 * 10 | Showing / of                                     | VAS_Showing / VAS_Of (shared global keys)
 * 11 | Couldn't load / Close                             | VAS_CouldntLoad / Close (shared global keys)
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
    // .MPC-utm-row / .MPC-utm-list are sized in em so 8 rows always fit
    // without a scrollbar, by CSS construction, at every screen size.
    var PAGE_SIZE = 8;

    VAS.VAS_133_UpdatedThisMonthWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var root = document.createElement('div');
        root.className = 'MPC-utm-root';

        var state = {
            updateCount: 0,
            userCount: 0,
            todayCount: 0,
            productCount: 0,
            monthStart: '',
            summaryLoaded: false,
            offset: 0,
            total: 0,
            rows: []
        };

        var els = {};
        var summaryController = null;
        var pageController = null;
        var pageToken = 0;
        var lastFocusedEl = null;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function pluralize(n, singularKey, singularFallback, pluralKey, pluralFallback) {
            return n === 1 ? lbl(singularKey, singularFallback) : lbl(pluralKey, pluralFallback);
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

        function parseLocalDate(value) {
            if (!value) { return null; }
            var str = String(value).trim();
            var parts = str.split(' ');
            if (parts.length < 2) { parts = str.split('T'); }
            if (parts.length < 2) { return new Date(str); }
            var dateParts = parts[0].split('-');
            var timeParts = parts[1].split(':');
            if (dateParts.length < 3 || timeParts.length < 2) { return new Date(str); }
            return new Date(
                parseInt(dateParts[0], 10),
                parseInt(dateParts[1], 10) - 1,
                parseInt(dateParts[2], 10),
                parseInt(timeParts[0], 10),
                parseInt(timeParts[1], 10),
                parseInt(timeParts[2] || '0', 10)
            );
        }

        function formatDate(value) {
            if (!value) { return ''; }
            var date = parseLocalDate(value);
            if (!date || isNaN(date.getTime())) { return String(value); }
            return date.toLocaleDateString(window.navigator.language, { day: 'numeric', month: 'long', year: 'numeric' });
        }

        function formatRelativeTime(value) {
            if (!value) { return ''; }
            var date = parseLocalDate(value);
            if (!date || isNaN(date.getTime())) { return String(value); }
            var diffMs = Date.now() - date.getTime();
            var diffMin = Math.floor(diffMs / 60000);
            if (diffMin < 1) { return lbl('VAS_133_UTM_JustNow', 'Just now'); }
            if (diffMin < 60) { return diffMin + lbl('VAS_133_UTM_MinsAgo', 'm ago'); }
            var diffHour = Math.floor(diffMin / 60);
            if (diffHour < 24) { return diffHour + lbl('VAS_133_UTM_HoursAgo', 'h ago'); }
            var diffDay = Math.floor(diffHour / 24);
            var dayWord = pluralize(diffDay, 'VAS_133_UTM_DayWord', 'day', 'VAS_133_UTM_DaysWord', 'days');
            return diffDay + ' ' + dayWord + ' ' + lbl('VAS_133_UTM_DaysAgo', 'ago');
        }

        /* ---- Build the static DOM once ---- */
        function build() {
            var card = el('button', 'MPC-utm-card');
            card.type = 'button';
            card.appendChild(el('span', 'MPC-utm-title', lbl('VAS_133_UTM_Title', 'Updated This Month')));

            var body = el('div', 'MPC-utm-body');
            var value = el('div', 'MPC-utm-value', '…');
            var meta = el('div', 'MPC-utm-meta', '');
            body.appendChild(value);
            body.appendChild(meta);
            card.appendChild(body);
            card.addEventListener('click', function () { openModal(card); });

            root.appendChild(card);
            els.card = card;
            els.value = value;
            els.meta = meta;

            buildModal();
        }

        function buildModal() {
            var overlay = el('div', 'MPC-utm-overlay');
            overlay.setAttribute('aria-hidden', 'true');
            var panel = el('div', 'MPC-utm-panel');
            panel.setAttribute('role', 'dialog');
            panel.setAttribute('aria-modal', 'true');
            panel.setAttribute('aria-labelledby', 'MPC-utm-title-' + Math.random().toString(36).slice(2));
            var titleId = panel.getAttribute('aria-labelledby');

            var head = el('div', 'MPC-utm-p-head');
            var title = el('span', 'MPC-utm-p-title', lbl('VAS_133_UTM_Title', 'Updated This Month'));
            title.id = titleId;
            var close = el('button', 'MPC-utm-p-close');
            close.type = 'button';
            close.setAttribute('aria-label', lbl('Close', 'Close'));
            close.appendChild(svg('<path d="M18 6 6 18"/><path d="m6 6 12 12"/>'));
            close.addEventListener('click', closeModal);
            head.appendChild(title);
            head.appendChild(close);

            var pbody = el('div', 'MPC-utm-p-body');
            var intro = el('p', 'MPC-utm-intro');
            var sectionHead = el('div', 'MPC-utm-section-head');
            sectionHead.appendChild(el('span', 'MPC-utm-section-title', lbl('VAS_133_UTM_SectionTitle', 'Recent updates')));
            var summary = el('span', 'MPC-utm-section-summary');
            sectionHead.appendChild(summary);
            var list = el('div', 'MPC-utm-list');
            pbody.appendChild(intro);
            pbody.appendChild(sectionHead);
            pbody.appendChild(list);

            var foot = el('div', 'MPC-utm-p-foot');
            var helper = el('span', 'MPC-utm-helper');
            var pager = el('span', 'MPC-utm-pager');
            var prev = el('button', 'MPC-utm-pgbtn');
            prev.type = 'button';
            prev.setAttribute('aria-label', lbl('VAS_PreviousPage', 'Previous page'));
            prev.appendChild(svg('<path d="m15 18-6-6 6-6"/>'));
            var pageText = el('span', 'MPC-utm-pgtext', '1 ' + lbl('VAS_Of', 'of') + ' 1');
            var next = el('button', 'MPC-utm-pgbtn');
            next.type = 'button';
            next.setAttribute('aria-label', lbl('VAS_NextPage', 'Next page'));
            next.appendChild(svg('<path d="m9 18 6-6-6-6"/>'));
            prev.addEventListener('click', function () {
                if (state.offset <= 0) { return; }
                loadPage(Math.max(0, state.offset - PAGE_SIZE));
            });
            next.addEventListener('click', function () {
                var nextOffset = state.offset + PAGE_SIZE;
                if (nextOffset >= state.total) { return; }
                loadPage(nextOffset);
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

            overlay.addEventListener('mousedown', function (e) { if (e.target === overlay) { closeModal(); } });
            overlay.addEventListener('keydown', function (e) {
                if (e.key === 'Escape') { closeModal(); return; }
                if (e.key === 'Tab') {
                    var focusables = panel.querySelectorAll('button');
                    if (!focusables.length) { return; }
                    var first = focusables[0], last = focusables[focusables.length - 1];
                    if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
                    else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
                }
            });

            document.body.appendChild(overlay);
            els.overlay = overlay;
            els.panel = panel;
            els.intro = intro;
            els.summary = summary;
            els.list = list;
            els.helper = helper;
            els.pageText = pageText;
            els.prev = prev;
            els.next = next;
            els.close = close;
        }

        /* ---- Summary (tile) ---- */
        function loadSummary() {
            if (summaryController && typeof summaryController.abort === 'function') {
                try { summaryController.abort(); } catch (ignored) { }
            }
            summaryController = (typeof AbortController !== 'undefined') ? new AbortController() : null;

            var url = VIS.Application.contextUrl + 'VAS_133_UpdatedThisMonthWidget/GetSummary';
            fetch(url, {
                method: 'GET',
                credentials: 'same-origin',
                signal: summaryController ? summaryController.signal : undefined
            }).then(function (res) { return res.text(); }).then(function (text) {
                var data = parseResponse(text);
                if (!data || data.error) { showSummaryError(); return; }
                state.updateCount = data.updateCount || 0;
                state.userCount = data.userCount || 0;
                state.todayCount = data.todayCount || 0;
                state.productCount = data.productCount || 0;
                state.monthStart = data.monthStart || '';
                state.summaryLoaded = true;
                renderSummary();
            }).catch(function (err) {
                if (err && err.name === 'AbortError') { return; }
                showSummaryError();
            });
        }

        function renderSummary() {
            els.value.textContent = String(state.updateCount);
            els.meta.textContent = lbl('VAS_133_UTM_Meta', 'Updates by') + ' ' + state.userCount + ' ' + lbl('VAS_133_UTM_UsersWord', 'users') +
                ' · ' + state.todayCount + ' ' + lbl('VAS_133_UTM_TodayWord', 'today');
        }

        function showSummaryError() {
            els.value.textContent = '—';
            els.meta.textContent = lbl('VAS_CouldntLoad', "Couldn't load");
        }

        function parseResponse(text) {
            var data = text;
            try {
                if (typeof data === 'string' && data.length) { data = JSON.parse(data); }
                if (typeof data === 'string' && data.length) { data = JSON.parse(data); }
            } catch (e) { return null; }
            return data || {};
        }

        /* ---- Modal ---- */
        function openModal(triggerEl) {
            lastFocusedEl = triggerEl || null;
            state.offset = 0;

            els.intro.textContent = lbl('VAS_133_UTM_Intro', 'Item master changes recorded since') + ' ' + formatDate(state.monthStart) +
                ' · ' + state.productCount + ' ' + lbl('VAS_133_UTM_ProductsWord', 'products') +
                ' · ' + state.userCount + ' ' + lbl('VAS_133_UTM_UsersWord', 'users') + '.';

            els.overlay.classList.add('MPC-utm-open');
            els.overlay.setAttribute('aria-hidden', 'false');
            document.body.classList.add('MPC-utm-body-lock');

            showListMessage(lbl('Loading', 'Loading...'));
            loadPage(0);

            els.close.focus();
        }

        function closeModal() {
            if (!els.overlay) { return; }
            els.overlay.classList.remove('MPC-utm-open');
            els.overlay.setAttribute('aria-hidden', 'true');
            document.body.classList.remove('MPC-utm-body-lock');
            if (lastFocusedEl && lastFocusedEl.focus) { lastFocusedEl.focus(); }
            lastFocusedEl = null;
        }

        function loadPage(offset) {
            if (pageController && typeof pageController.abort === 'function') {
                try { pageController.abort(); } catch (ignored) { }
            }
            pageController = (typeof AbortController !== 'undefined') ? new AbortController() : null;
            var myToken = ++pageToken;

            var url = VIS.Application.contextUrl + 'VAS_133_UpdatedThisMonthWidget/GetPagedUpdates?offset=' + encodeURIComponent(offset) + '&pageSize=' + encodeURIComponent(PAGE_SIZE);
            fetch(url, {
                method: 'GET',
                credentials: 'same-origin',
                signal: pageController ? pageController.signal : undefined
            }).then(function (res) { return res.text(); }).then(function (text) {
                if (myToken !== pageToken) { return; } // stale response
                var data = parseResponse(text);
                if (!data || data.error) { showListError(offset); return; }
                state.offset = data.offset || 0;
                state.total = data.total || 0;
                state.rows = data.rows || [];
                renderList();
            }).catch(function (err) {
                if (myToken !== pageToken) { return; }
                if (err && err.name === 'AbortError') { return; }
                showListError(offset);
            });
        }

        function showListMessage(message) {
            els.list.innerHTML = '';
            els.list.appendChild(el('div', 'MPC-utm-state', message));
            els.summary.textContent = '';
            els.helper.textContent = '';
            els.pageText.textContent = '1 ' + lbl('VAS_Of', 'of') + ' 1';
            els.prev.disabled = true;
            els.next.disabled = true;
        }

        function showListError(offset) {
            els.list.innerHTML = '';
            var wrap = el('div', 'MPC-utm-state');
            wrap.appendChild(el('span', null, lbl('VAS_CouldntLoad', "Couldn't load")));
            var retry = el('button', 'MPC-utm-retry', lbl('VAS_133_UTM_Retry', 'Retry'));
            retry.type = 'button';
            retry.addEventListener('click', function () { showListMessage(lbl('Loading', 'Loading...')); loadPage(offset); });
            wrap.appendChild(retry);
            els.list.appendChild(wrap);
            els.summary.textContent = '';
            els.helper.textContent = '';
            els.pageText.textContent = '1 ' + lbl('VAS_Of', 'of') + ' 1';
            els.prev.disabled = true;
            els.next.disabled = true;
        }

        function buildRow(row) {
            var rowEl = el('div', 'MPC-utm-row');

            var left = el('div', 'MPC-utm-row-left');
            left.appendChild(el('div', 'MPC-utm-row-primary', row.productName || ''));
            var updatedByName = row.updatedByName && String(row.updatedByName).trim()
                ? row.updatedByName
                : lbl('VAS_133_UTM_UnknownUser', 'Unknown user');
            var metaText = (row.description || '') + ' · ' + updatedByName + ' · ' + formatRelativeTime(row.updatedAt);
            left.appendChild(el('div', 'MPC-utm-row-meta', metaText));
            rowEl.appendChild(left);

            var right = el('div', 'MPC-utm-row-right');
            right.appendChild(el('span', 'MPC-utm-pill', row.category || ''));
            rowEl.appendChild(right);

            return rowEl;
        }

        function pageCount() {
            return Math.max(1, Math.ceil(state.total / PAGE_SIZE));
        }

        function renderList() {
            var total = state.total;

            if (!total) {
                els.list.innerHTML = '';
                els.list.appendChild(el('div', 'MPC-utm-state', lbl('VAS_133_UTM_EmptyState', 'No product or price records were updated this month.')));
                els.summary.textContent = 0 + ' ' + lbl('VAS_133_UTM_SectionSummary', 'updates');
                els.helper.textContent = '';
                els.pageText.textContent = '1 ' + lbl('VAS_Of', 'of') + ' 1';
                els.prev.disabled = true;
                els.next.disabled = true;
                return;
            }

            els.list.innerHTML = '';
            state.rows.forEach(function (row) { els.list.appendChild(buildRow(row)); });

            var pages = pageCount();
            var currentPage = Math.floor(state.offset / PAGE_SIZE) + 1;
            var from = state.offset + 1;
            var to = Math.min(state.offset + state.rows.length, total);

            els.summary.textContent = total + ' ' + lbl('VAS_133_UTM_SectionSummary', 'updates');
            els.helper.textContent = lbl('VAS_Showing', 'Showing') + ' ' + from + '–' + to + ' ' + lbl('VAS_Of', 'of') + ' ' + total;
            els.pageText.textContent = currentPage + ' ' + lbl('VAS_Of', 'of') + ' ' + pages;
            els.prev.disabled = state.offset <= 0;
            els.next.disabled = (state.offset + PAGE_SIZE) >= total;
        }

        /* ---- Lifecycle ---- */
        this.Initalize = function () {
            build();
            loadSummary();
        };

        this.refreshWidget = function () {
            closeModal();
            loadSummary();
        };

        this.getRoot = function () { return root; };

        this.disposeComponent = function () {
            if (summaryController && typeof summaryController.abort === 'function') {
                try { summaryController.abort(); } catch (ignored) { }
            }
            if (pageController && typeof pageController.abort === 'function') {
                try { pageController.abort(); } catch (ignored) { }
            }
            if (els.overlay && els.overlay.parentNode) { els.overlay.parentNode.removeChild(els.overlay); }
            document.body.classList.remove('MPC-utm-body-lock');
            if (root.parentNode) { root.parentNode.removeChild(root); }
        };
    };

    VAS.VAS_133_UpdatedThisMonthWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_133_UpdatedThisMonthWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_133_UpdatedThisMonthWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_133_UpdatedThisMonthWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_133_UpdatedThisMonthWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_133_UpdatedThisMonthWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
