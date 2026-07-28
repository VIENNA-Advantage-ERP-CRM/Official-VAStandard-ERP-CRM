/**
 * Open DOs Widget (Delivery Order dashboard)
 * Widget number 152.
 * Widget size: 2 columns x 1 row.
 * Display-only KPI tile: count of active outbound customer Delivery Order
 * headers not yet shipped - customer shipment documents (MovementType 'C-')
 * that are sales, non-return transactions in Drafted (DR), In Progress (IP)
 * or Waiting Confirmation (WC) status. Counts Delivery Order headers, never
 * Delivery Order lines. Read-only, not clickable, no modal. Plain DOM
 * internals (no jQuery in the widget logic); jQuery is used only at the
 * framework boundary.
 * Backend - VAS_152_OpenDOsWidget/GetSummary
 * Summary Message Table
 *  # | Current Text                          | Message Key
 * ---+----------------------------------------+------------------------
 *  1 | Open DOs                              | VAS_152_ODO_Title
 *  2 | Ready to pick                         | VAS_152_ODO_Meta
 *  3 | Data unavailable                      | VAS_152_ODO_DataUnavailable
 *  4 | open delivery orders (count prepended in code, used as aria-label) | VAS_152_ODO_AriaSuffix
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

    VAS.VAS_152_OpenDOsWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var root = document.createElement('div');
        root.className = 'MPC-odo-root';

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

        /* ---- Build the static DOM once ---- */
        function build() {
            var card = el('div', 'MPC-odo-card');
            card.setAttribute('aria-label', lbl('VAS_152_ODO_Title', 'Open DOs'));
            card.appendChild(el('span', 'MPC-odo-title', lbl('VAS_152_ODO_Title', 'Open DOs')));

            var body = el('div', 'MPC-odo-body');
            var value = el('div', 'MPC-odo-value', '…');
            var meta = el('div', 'MPC-odo-meta', lbl('VAS_152_ODO_Meta', 'Ready to pick'));
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

            var url = VIS.Application.contextUrl + 'VAS_152_OpenDOsWidget/GetSummary';
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
            var count = data.openDoCount || 0;
            els.value.textContent = String(count);
            els.meta.textContent = lbl('VAS_152_ODO_Meta', 'Ready to pick');
            els.card.setAttribute('aria-label', count + ' ' + lbl('VAS_152_ODO_AriaSuffix', 'open delivery orders'));
        }

        function showError() {
            els.value.textContent = '--';
            els.meta.textContent = lbl('VAS_152_ODO_DataUnavailable', 'Data unavailable');
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

    VAS.VAS_152_OpenDOsWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_152_OpenDOsWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_152_OpenDOsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_152_OpenDOsWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_152_OpenDOsWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_152_OpenDOsWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
