/**
 * On-Time Rate Widget (Delivery Order dashboard)
 * Widget number 151.
 * Widget size: 2 columns x 1 row.
 * Display-only KPI tile: month-to-date percentage of outbound customer
 * Delivery Orders (MovementType 'C-', sales, non-return, CO/CL) shipped on or
 * before the latest resolved promised date of their linked Sales Order. The
 * percentage is computed entirely in the backend; this widget only formats
 * the returned number (up to two decimals, trailing zeros trimmed) and
 * appends '%'. Counts DO headers, never lines. Read-only, not clickable, no
 * modal. Plain DOM internals (no jQuery in the widget logic); jQuery is used
 * only at the framework boundary.
 * Backend - VAS_151_OnTimeRateWidget/GetSummary
 * Summary Message Table
 *  # | Current Text                          | Message Key
 * ---+----------------------------------------+------------------------
 *  1 | On-Time Rate                          | VAS_151_OTR_Title
 *  2 | MTD                                   | VAS_151_OTR_Meta
 *  3 | Data unavailable                      | VAS_151_OTR_DataUnavailable
 *  4 | on-time delivery rate month to date (percent appended in code, aria) | VAS_151_OTR_Aria
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

    VAS.VAS_151_OnTimeRateWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var root = document.createElement('div');
        root.className = 'MPC-otr-root';

        var els = {};
        var loadController = null;
        var loadToken = 0;

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

        /* Format the backend percentage for display: cap at two decimals and
           drop trailing zeros (94.00 -> 94, 94.50 -> 94.5). Does not recompute
           the rate - the backend already produced the percentage. */
        function formatRate(raw) {
            var n = Number(raw);
            if (!isFinite(n)) { return '0'; }
            n = Math.round(n * 100) / 100;
            return String(n);
        }

        /* ---- Build the static DOM once ---- */
        function build() {
            var card = el('div', 'MPC-otr-card');
            card.setAttribute('aria-label', lbl('VAS_151_OTR_Title', 'On-Time Rate'));
            card.appendChild(el('span', 'MPC-otr-title', lbl('VAS_151_OTR_Title', 'On-Time Rate')));

            var body = el('div', 'MPC-otr-body');
            var value = el('div', 'MPC-otr-value MPC-otr-neutral', '…');
            var meta = el('div', 'MPC-otr-meta', lbl('VAS_151_OTR_Meta', 'MTD'));
            body.appendChild(value);
            body.appendChild(meta);
            card.appendChild(body);

            root.appendChild(card);
            els.card = card;
            els.value = value;
            els.meta = meta;
        }

        /* ---- Data load ---- */
        function loadSummary() {
            if (loadController && typeof loadController.abort === 'function') {
                try { loadController.abort(); } catch (ignored) { }
            }
            loadController = (typeof AbortController !== 'undefined') ? new AbortController() : null;
            var myToken = ++loadToken;

            var url = VIS.Application.contextUrl + 'VAS_151_OnTimeRateWidget/GetSummary';
            fetch(url, {
                method: 'GET',
                credentials: 'same-origin',
                signal: loadController ? loadController.signal : undefined
            }).then(function (res) { return res.text(); }).then(function (text) {
                if (myToken !== loadToken) { return; } // stale response
                var data = parseResponse(text);
                if (!data || data.error) { showError(); return; }
                showSummary(data);
            }).catch(function (err) {
                if (myToken !== loadToken) { return; }
                if (err && err.name === 'AbortError') { return; }
                showError();
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

        function showSummary(data) {
            var display = formatRate(data.onTimeRate);
            els.value.classList.remove('MPC-otr-neutral');
            els.value.textContent = display + '%';
            els.meta.textContent = lbl('VAS_151_OTR_Meta', 'MTD');
            els.card.setAttribute('aria-label',
                lbl('VAS_151_OTR_Aria', 'on-time delivery rate month to date') + ': ' + display + '%');
        }

        function showError() {
            els.value.classList.add('MPC-otr-neutral');
            els.value.textContent = '--%';
            els.meta.textContent = lbl('VAS_151_OTR_Meta', 'MTD');
        }

        /* ---- Lifecycle ---- */
        this.Initalize = function () {
            build();
            loadSummary();
        };

        this.refreshWidget = function () {
            loadSummary();
        };

        this.getRoot = function () { return root; };

        this.disposeComponent = function () {
            if (loadController && typeof loadController.abort === 'function') {
                try { loadController.abort(); } catch (ignored) { }
            }
            if (root.parentNode) { root.parentNode.removeChild(root); }
        };
    };

    VAS.VAS_151_OnTimeRateWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_151_OnTimeRateWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_151_OnTimeRateWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_151_OnTimeRateWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_151_OnTimeRateWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_151_OnTimeRateWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
