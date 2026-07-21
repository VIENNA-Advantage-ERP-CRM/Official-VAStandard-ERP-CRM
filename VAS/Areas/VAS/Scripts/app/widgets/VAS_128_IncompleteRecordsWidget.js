/**
 * Incomplete Records Widget (Product Master dashboard)
 * Widget number 128.
 * Widget size: 5 columns x 3 rows.
 * Data-hygiene worklist of active, non-summary products missing one or more
 * of the master-data fields the CURRENT USER tracks. Each row shows the
 * product, its (mandatory, never-tracked) category, red chips for the
 * selected missing fields, and the last-updated date + user. A gear toggles
 * an in-widget settings view (15-field checkbox grid) whose changes persist
 * immediately as one AD_Preference row per field; a row click opens a detail
 * modal with an "Open Record" button that navigates to the Product Master
 * record. Fit-to-height pagination - the rows region never scrolls.
 * Verified is only a miss for BOM products. Read-only except the per-user
 * preference save. Plain DOM internals (no jQuery in the widget logic);
 * jQuery is used only at the framework boundary.
 * Backend - VAS_128_IncompleteRecordsWidget/GetIncompleteRecords
 *           VAS_128_IncompleteRecordsWidget/SaveFieldPreference
 * Summary Message Table
 *  # | Current Text                                     | Message Key
 * ---+--------------------------------------------------+------------------------
 *  1 | Incomplete Records                               | VAS_128_Title
 *  2 | Items missing master data · tap a row for detail | VAS_128_Subtitle
 *  3 | item / missing data / category / last updated    | VAS_128_ColItem / VAS_128_ColMissing / VAS_128_ColCategory / VAS_128_ColUpdated
 *  4 | by                                               | VAS_128_By
 *  5 | Showing / of / incomplete                        | VAS_Showing / VAS_Of / VAS_128_Incomplete
 *  6 | fields tracked                                   | VAS_128_FieldsTracked
 *  7 | No items are missing the selected data...        | VAS_128_EmptyState
 *  8 | Records are filtered to items missing...         | VAS_128_SettingsExplainer
 *  9 | Select all / Clear / Done                        | VAS_128_SelectAll / VAS_128_Clear / VAS_128_Done
 * 10 | (BOM only)                                       | VAS_128_BomOnly
 * 11 | Missing data ({0} of {1} tracked fields)         | VAS_128_MissingCaption
 * 12 | Missing                                          | VAS_128_Missing
 * 13 | Only fields selected in the widget's preference. | VAS_128_ModalNote
 * 14 | Close / Open Record                              | Close / VAS_128_OpenRecord
 * 15 | Couldn't load / Retry                            | VAS_CouldntLoad / VAS_128_Retry
 * 16 | Preference not saved.                            | VAS_128_PrefNotSaved
 * 17 | Configure missing data                           | VAS_128_ConfigureAria
 * 18 | Field labels (HSN, Barcode, ...)                 | VAS_128_Field<Key>
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

    VAS.VAS_128_IncompleteRecordsWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var root = document.createElement('div');
        root.className = 'MPC-ir-root';

        // The fixed 15-field catalog, in chip/settings order (matches the
        // controller). bomOnly carries the muted "(BOM only)" note.
        var FIELDS = [
            { key: 'hsn', msg: 'VAS_128_FieldHsn', fallback: 'HSN' },
            { key: 'barcode', msg: 'VAS_128_FieldBarcode', fallback: 'Barcode' },
            { key: 'sku', msg: 'VAS_128_FieldSku', fallback: 'SKU' },
            { key: 'image', msg: 'VAS_128_FieldImage', fallback: 'Image' },
            { key: 'description', msg: 'VAS_128_FieldDescription', fallback: 'Description' },
            { key: 'brand', msg: 'VAS_128_FieldBrand', fallback: 'Brand' },
            { key: 'salesRep', msg: 'VAS_128_FieldSalesRep', fallback: 'Sales Rep' },
            { key: 'revenueRecognition', msg: 'VAS_128_FieldRevenueRecognition', fallback: 'Revenue Recognition' },
            { key: 'mailTemplate', msg: 'VAS_128_FieldMailTemplate', fallback: 'Mail Template' },
            { key: 'qualityCriteria', msg: 'VAS_128_FieldQualityCriteria', fallback: 'Quality Criteria' },
            { key: 'verified', msg: 'VAS_128_FieldVerified', fallback: 'Verified', bomOnly: true },
            { key: 'preferredVendor', msg: 'VAS_128_FieldPreferredVendor', fallback: 'Preferred Vendor' },
            { key: 'guaranteeDetails', msg: 'VAS_128_FieldGuaranteeDetails', fallback: 'Guarantee Details' },
            { key: 'attributeGroup', msg: 'VAS_128_FieldAttributeGroup', fallback: 'Attribute Group' },
            { key: 'customTariff', msg: 'VAS_128_FieldCustomTariff', fallback: 'Custom Tariff' }
        ];
        var FIELD_BY_KEY = {};
        FIELDS.forEach(function (f) { FIELD_BY_KEY[f.key] = f; });

        var MAX_PER_PAGE = 12;

        var state = {
            mode: 'list',
            items: [],
            selected: [],        // tracked field keys
            available: [],       // field keys usable on this DB
            page: 1,
            perPage: MAX_PER_PAGE,
            firstIndex: 0,
            activeItem: null,
            loaded: false
        };

        var els = {};
        var loadController = null;
        var resizeObserver = null;
        var resizeTimer = null;
        var lastFocusedRow = null;

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

        function fieldLabel(key) {
            var f = FIELD_BY_KEY[key];
            return f ? lbl(f.msg, f.fallback) : key;
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
            if (isNaN(date.getTime())) {
                var parts = String(value).split(/[- :]/);
                if (parts.length >= 3) {
                    date = new Date(Number(parts[0]), Number(parts[1]) - 1, Number(parts[2]));
                }
            }
            if (isNaN(date.getTime())) { return String(value); }
            return date.toLocaleDateString(window.navigator.language, { day: '2-digit', month: 'short', year: 'numeric' });
        }

        function selectedCount() { return state.selected.length; }

        function isTracked(key) { return state.selected.indexOf(key) >= 0; }

        /* ---- Build the static DOM once ---- */
        function build() {
            // Header
            var head = el('div', 'MPC-ir-head');
            var headLeft = el('div', 'MPC-ir-head-left');
            var icon = el('span', 'MPC-ir-ico');
            icon.appendChild(svg('<path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3"/><path d="M12 9v4"/><path d="M12 17h.01"/>'));
            var titles = el('div', 'MPC-ir-titles');
            titles.appendChild(el('div', 'MPC-ir-title', lbl('VAS_128_Title', 'Incomplete Records')));
            titles.appendChild(el('div', 'MPC-ir-subtitle', lbl('VAS_128_Subtitle', 'Items missing master data · tap a row for detail')));
            headLeft.appendChild(icon);
            headLeft.appendChild(titles);

            var gear = el('button', 'MPC-ir-gear');
            gear.type = 'button';
            gear.setAttribute('aria-label', lbl('VAS_128_ConfigureAria', 'Configure missing data'));
            gear.title = lbl('VAS_128_ConfigureAria', 'Configure missing data');
            gear.appendChild(svg('<path d="M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z"/><circle cx="12" cy="12" r="3"/>'));
            gear.addEventListener('click', function () { setMode(state.mode === 'settings' ? 'list' : 'settings'); });
            els.gear = gear;

            head.appendChild(headLeft);
            head.appendChild(gear);

            // List state
            var listState = el('div', 'MPC-ir-list');
            var gridHead = el('div', 'MPC-ir-row MPC-ir-ghead');
            gridHead.appendChild(el('span', null, lbl('VAS_128_ColItem', 'item')));
            gridHead.appendChild(el('span', null, lbl('VAS_128_ColMissing', 'missing data')));
            gridHead.appendChild(el('span', null, lbl('VAS_128_ColCategory', 'category')));
            gridHead.appendChild(el('span', null, lbl('VAS_128_ColUpdated', 'last updated')));
            var rowsClip = el('div', 'MPC-ir-rows');
            var foot = el('div', 'MPC-ir-foot');
            var helper = el('span', 'MPC-ir-helper');
            var pager = el('span', 'MPC-ir-pager');
            var prev = el('button', 'MPC-ir-pgbtn');
            prev.type = 'button';
            prev.setAttribute('aria-label', lbl('VAS_PreviousPage', 'Previous page'));
            prev.appendChild(svg('<path d="m15 18-6-6 6-6"/>'));
            var pageText = el('span', 'MPC-ir-pgtext', '1 ' + lbl('VAS_Of', 'of') + ' 1');
            var next = el('button', 'MPC-ir-pgbtn');
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
            foot.appendChild(helper);
            foot.appendChild(pager);
            listState.appendChild(gridHead);
            listState.appendChild(rowsClip);
            listState.appendChild(foot);
            els.rows = rowsClip;
            els.helper = helper;
            els.pageText = pageText;
            els.prev = prev;
            els.next = next;
            els.listState = listState;

            // Settings state
            var settingsState = el('div', 'MPC-ir-settings');
            settingsState.style.display = 'none';
            var setTop = el('div', 'MPC-ir-set-top');
            setTop.appendChild(el('div', 'MPC-ir-set-explain', lbl('VAS_128_SettingsExplainer', 'Records are filtered to items missing at least one selected field. Your selection is saved for you.')));
            var setLinks = el('div', 'MPC-ir-set-links');
            var selAll = el('button', 'MPC-ir-link', lbl('VAS_128_SelectAll', 'Select all'));
            selAll.type = 'button';
            var clearAll = el('button', 'MPC-ir-link', lbl('VAS_128_Clear', 'Clear'));
            clearAll.type = 'button';
            selAll.addEventListener('click', function () { bulkSet(true); });
            clearAll.addEventListener('click', function () { bulkSet(false); });
            setLinks.appendChild(selAll);
            setLinks.appendChild(clearAll);
            setTop.appendChild(setLinks);
            var setGrid = el('div', 'MPC-ir-set-grid');
            var setFoot = el('div', 'MPC-ir-foot MPC-ir-set-foot');
            var setErr = el('span', 'MPC-ir-set-err');
            var done = el('button', 'MPC-ir-done', lbl('VAS_128_Done', 'Done'));
            done.type = 'button';
            done.addEventListener('click', function () { setMode('list'); });
            setFoot.appendChild(setErr);
            setFoot.appendChild(done);
            settingsState.appendChild(setTop);
            settingsState.appendChild(setGrid);
            settingsState.appendChild(setFoot);
            els.setGrid = setGrid;
            els.setErr = setErr;
            els.settingsState = settingsState;

            var card = el('div', 'MPC-ir-card');
            card.appendChild(head);
            card.appendChild(listState);
            card.appendChild(settingsState);
            root.appendChild(card);

            buildModal();
        }

        function buildModal() {
            var overlay = el('div', 'MPC-ir-overlay');
            overlay.setAttribute('aria-hidden', 'true');
            var modal = el('div', 'MPC-ir-modal');
            modal.setAttribute('role', 'dialog');
            modal.setAttribute('aria-modal', 'true');

            var mHead = el('div', 'MPC-ir-m-head');
            var mHeadText = el('div', 'MPC-ir-m-headtext');
            var mTitle = el('div', 'MPC-ir-m-title');
            var mSub = el('div', 'MPC-ir-m-sub');
            mHeadText.appendChild(mTitle);
            mHeadText.appendChild(mSub);
            var mClose = el('button', 'MPC-ir-m-close');
            mClose.type = 'button';
            mClose.setAttribute('aria-label', lbl('Close', 'Close'));
            mClose.appendChild(svg('<path d="M18 6 6 18"/><path d="m6 6 12 12"/>'));
            mClose.addEventListener('click', closeModal);
            mHead.appendChild(mHeadText);
            mHead.appendChild(mClose);

            var mBody = el('div', 'MPC-ir-m-body');

            var mFoot = el('div', 'MPC-ir-m-foot');
            var mCancel = el('button', 'MPC-ir-btn-ghost', lbl('Close', 'Close'));
            mCancel.type = 'button';
            mCancel.addEventListener('click', closeModal);
            var mOpen = el('button', 'MPC-ir-btn-primary');
            mOpen.type = 'button';
            mOpen.appendChild(document.createTextNode(lbl('VAS_128_OpenRecord', 'Open Record') + ' '));
            mOpen.appendChild(svg('<path d="M15 3h6v6"/><path d="M10 14 21 3"/><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/>'));
            mOpen.addEventListener('click', openActiveRecord);
            mFoot.appendChild(mCancel);
            mFoot.appendChild(mOpen);

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
        }

        /* ---- Mode / data ---- */
        function setMode(mode) {
            state.mode = mode;
            els.listState.style.display = mode === 'list' ? 'flex' : 'none';
            els.settingsState.style.display = mode === 'settings' ? 'flex' : 'none';
            if (mode === 'settings') {
                renderSettings();
            } else {
                // Returning to the list re-derives everything from the (already
                // persisted) selection by reloading from the server.
                loadData();
            }
        }

        function loadData() {
            if (loadController && typeof loadController.abort === 'function') {
                try { loadController.abort(); } catch (ignored) { }
            }
            loadController = (typeof AbortController !== 'undefined') ? new AbortController() : null;

            showRowsMessage(lbl('Loading', 'Loading...'));

            var url = VIS.Application.contextUrl + 'VAS_128_IncompleteRecordsWidget/GetIncompleteRecords';
            fetch(url, {
                method: 'GET',
                credentials: 'same-origin',
                signal: loadController ? loadController.signal : undefined
            }).then(function (res) { return res.text(); }).then(function (text) {
                var data = parseResponse(text);
                if (!data || data.error) { showRowsError(); return; }
                state.selected = data.trackedFields || [];
                state.available = data.available || data.availableFields || [];
                state.items = data.items || [];
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
            return Math.max(1, Math.ceil(state.items.length / state.perPage));
        }

        function measurePerPage() {
            var region = els.rows;
            if (!region || !state.items.length) { return state.perPage; }
            // Probe: render one real row, measure, then clear.
            region.innerHTML = '';
            var probe = buildRow(state.items[0]);
            region.appendChild(probe);
            var rowH = probe.offsetHeight || 1;
            var regionH = region.clientHeight || 1;
            region.innerHTML = '';
            return Math.max(1, Math.min(MAX_PER_PAGE, Math.floor(regionH / rowH)));
        }

        function renderList(keepIndex) {
            if (!state.items.length) {
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
            var total = state.items.length;

            if (!total) { showEmptyState(); return; }

            var pages = pageCount();
            if (state.page > pages) { state.page = pages; }
            if (state.page < 1) { state.page = 1; }
            state.firstIndex = (state.page - 1) * state.perPage;
            var slice = state.items.slice(state.firstIndex, state.firstIndex + state.perPage);

            slice.forEach(function (item) { region.appendChild(buildRow(item)); });

            var from = total ? state.firstIndex + 1 : 0;
            var to = state.firstIndex + slice.length;
            els.helper.textContent =
                lbl('VAS_Showing', 'Showing') + ' ' + from + '–' + to + ' ' + lbl('VAS_Of', 'of') + ' ' + total +
                ' ' + lbl('VAS_128_Incomplete', 'incomplete') + ' · ' + selectedCount() + ' ' + lbl('VAS_128_FieldsTracked', 'fields tracked');
            els.pageText.textContent = state.page + ' ' + lbl('VAS_Of', 'of') + ' ' + pages;
            els.prev.disabled = state.page <= 1;
            els.next.disabled = state.page >= pages;
        }

        function buildRow(item) {
            var row = el('button', 'MPC-ir-row MPC-ir-grow');
            row.type = 'button';

            // Item cell (name + code)
            var itemCell = el('span', 'MPC-ir-c-item');
            itemCell.appendChild(el('span', 'MPC-ir-name', item.name || ''));
            itemCell.appendChild(el('span', 'MPC-ir-code', item.code || ''));

            // Missing chips (fixed catalog order, only tracked+missing)
            var chipCell = el('span', 'MPC-ir-c-chips');
            (item.missingKeys || []).forEach(function (key) {
                chipCell.appendChild(el('span', 'MPC-ir-chip', fieldLabel(key)));
            });

            // Category (em-dash when missing)
            var catCell = el('span', 'MPC-ir-c-cat', item.category && String(item.category).trim() ? item.category : '—');

            // Last updated (date + by user)
            var updCell = el('span', 'MPC-ir-c-upd');
            updCell.appendChild(el('span', 'MPC-ir-upd-date', formatDate(item.updatedAt)));
            updCell.appendChild(el('span', 'MPC-ir-upd-by', lbl('VAS_128_By', 'by') + ' ' + (item.updatedBy || '')));

            row.appendChild(itemCell);
            row.appendChild(chipCell);
            row.appendChild(catCell);
            row.appendChild(updCell);

            row.addEventListener('click', function () { openModal(item, row); });
            return row;
        }

        function showRowsMessage(message) {
            els.rows.innerHTML = '';
            els.rows.appendChild(el('div', 'MPC-ir-state', message));
            els.helper.textContent = '';
            els.pageText.textContent = '1 ' + lbl('VAS_Of', 'of') + ' 1';
            els.prev.disabled = true;
            els.next.disabled = true;
        }

        function showEmptyState() {
            els.rows.innerHTML = '';
            els.rows.appendChild(el('div', 'MPC-ir-state', lbl('VAS_128_EmptyState', 'No items are missing the selected data. Adjust tracked fields from the gear icon.')));
            els.helper.textContent =
                lbl('VAS_Showing', 'Showing') + ' 0 ' + lbl('VAS_Of', 'of') + ' 0 ' + lbl('VAS_128_Incomplete', 'incomplete') +
                ' · ' + selectedCount() + ' ' + lbl('VAS_128_FieldsTracked', 'fields tracked');
            els.pageText.textContent = '1 ' + lbl('VAS_Of', 'of') + ' 1';
            els.prev.disabled = true;
            els.next.disabled = true;
        }

        function showRowsError() {
            els.rows.innerHTML = '';
            var wrap = el('div', 'MPC-ir-state');
            wrap.appendChild(el('span', null, lbl('VAS_CouldntLoad', "Couldn't load")));
            var retry = el('button', 'MPC-ir-retry', lbl('VAS_128_Retry', 'Retry'));
            retry.type = 'button';
            retry.addEventListener('click', loadData);
            wrap.appendChild(retry);
            els.rows.appendChild(wrap);
            els.helper.textContent = '';
            els.pageText.textContent = '1 ' + lbl('VAS_Of', 'of') + ' 1';
            els.prev.disabled = true;
            els.next.disabled = true;
        }

        /* ---- Settings ---- */
        function renderSettings() {
            var grid = els.setGrid;
            grid.innerHTML = '';
            els.setErr.textContent = '';
            var available = state.available.length ? state.available : FIELDS.map(function (f) { return f.key; });

            FIELDS.forEach(function (f) {
                if (available.indexOf(f.key) < 0) { return; }
                var rowLabel = el('label', 'MPC-ir-set-row');
                var cb = document.createElement('input');
                cb.type = 'checkbox';
                cb.checked = isTracked(f.key);
                cb.setAttribute('data-key', f.key);
                cb.addEventListener('change', function () { onToggleField(f.key, cb.checked, cb); });
                var text = el('span', null, lbl(f.msg, f.fallback));
                if (f.bomOnly) {
                    text.appendChild(document.createTextNode(' '));
                    text.appendChild(el('span', 'MPC-ir-set-note', lbl('VAS_128_BomOnly', '(BOM only)')));
                }
                rowLabel.appendChild(cb);
                rowLabel.appendChild(text);
                grid.appendChild(rowLabel);
            });
        }

        function onToggleField(key, checked, cb) {
            // Optimistic local update; persist immediately.
            setLocalSelected(key, checked);
            saveField(key, checked, cb);
        }

        function setLocalSelected(key, checked) {
            var idx = state.selected.indexOf(key);
            if (checked && idx < 0) { state.selected.push(key); }
            else if (!checked && idx >= 0) { state.selected.splice(idx, 1); }
        }

        function bulkSet(selectAll) {
            var available = state.available.length ? state.available : FIELDS.map(function (f) { return f.key; });
            var previous = state.selected.slice();
            state.selected = selectAll ? available.slice() : [];
            renderSettings();
            // Persist only the fields whose value actually changed.
            available.forEach(function (key) {
                var was = previous.indexOf(key) >= 0;
                var now = selectAll;
                if (was !== now) { saveField(key, now, null); }
            });
        }

        function saveField(key, checked, cb) {
            els.setErr.textContent = '';
            var url = VIS.Application.contextUrl + 'VAS_128_IncompleteRecordsWidget/SaveFieldPreference';
            var body = 'fieldKey=' + encodeURIComponent(key) + '&value=' + (checked ? 'Y' : 'N');
            fetch(url, {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8' },
                body: body
            }).then(function (res) { return res.text(); }).then(function (text) {
                var data = parseResponse(text);
                if (!data || data.error || data.success === false) { onSaveFailed(key, checked, cb); }
            }).catch(function () { onSaveFailed(key, checked, cb); });
        }

        function onSaveFailed(key, checked, cb) {
            // Keep the working selection for the session; show a small,
            // non-blocking message. Do not crash or revert.
            if (els.setErr) { els.setErr.textContent = lbl('VAS_128_PrefNotSaved', 'Preference not saved.'); }
        }

        /* ---- Modal ---- */
        function openModal(item, rowEl) {
            state.activeItem = item;
            lastFocusedRow = rowEl || null;

            els.mTitle.textContent = item.name || '';
            els.mSub.textContent = (item.code || '') + ' · ' + (item.category && String(item.category).trim() ? item.category : '—') +
                ' · Updated ' + formatDate(item.updatedAt) + ' ' + lbl('VAS_128_By', 'by') + ' ' + (item.updatedBy || '');

            var body = els.mBody;
            body.innerHTML = '';
            var missing = item.missingKeys || [];
            body.appendChild(el('div', 'MPC-ir-m-caption', format('VAS_128_MissingCaption', 'Missing data ({0} of {1} tracked fields)', missing.length, selectedCount())));

            missing.forEach(function (key) {
                var mrow = el('div', 'MPC-ir-m-row');
                mrow.appendChild(el('span', 'MPC-ir-m-field', fieldLabel(key)));
                mrow.appendChild(el('span', 'MPC-ir-chip', lbl('VAS_128_Missing', 'Missing')));
                body.appendChild(mrow);
            });

            body.appendChild(el('div', 'MPC-ir-m-note', lbl('VAS_128_ModalNote', "Only fields selected in the widget's missing-data preference are shown.")));

            els.overlay.classList.add('MPC-ir-open');
            els.overlay.setAttribute('aria-hidden', 'false');
            document.body.classList.add('MPC-ir-body-lock');
            els.mClose.focus();
        }

        function closeModal() {
            if (!els.overlay) { return; }
            els.overlay.classList.remove('MPC-ir-open');
            els.overlay.setAttribute('aria-hidden', 'true');
            document.body.classList.remove('MPC-ir-body-lock');
            state.activeItem = null;
            if (lastFocusedRow && lastFocusedRow.focus) { lastFocusedRow.focus(); }
            lastFocusedRow = null;
        }

        // NOTE (2026-07-21): widgetFirevalueChanged without ActionName/
        // ActionType opens a NEW window every time, even if the Product
        // Master window is already open (confirmed against the ASI18 port
        // of this same widget, which had the identical bug). The framework
        // navigates IN-PLACE (same window, no duplicate) only when the
        // payload's ActionName matches the name of the window currently
        // HOSTING this widget - see VAS_091_MaterialReceiptSearchWidget.js's
        // zoomTo() (and 067/069/070/036/045) for the established, working
        // pattern this is copied from. hostWindowName() resolves the actual
        // host name via the widget's listener chain, so this keeps working
        // even if the widget is moved to a different host window later.
        function hostWindowName() {
            try {
                var l = $self.listener;
                for (var i = 0; i < 6 && l; i++) {
                    if (l.apanel && l.apanel.gridWindow && l.apanel.gridWindow.getName) {
                        return l.apanel.gridWindow.getName();
                    }
                    if (l.gridWindow && l.gridWindow.getName) {
                        return l.gridWindow.getName();
                    }
                    l = l.listener;
                }
            } catch (e) { }
            return '';
        }

        function openActiveRecord() {
            var item = state.activeItem;
            if (!item || !item.productId) { return; }
            // Navigate to the Product Master record through the widget framework's
            // value-changed channel (the host opens the configured product window
            // at this record). Not a prototype hash URL.
            var windowParam = {
                "TabWhereClause": "M_Product.M_Product_ID=" + Number(item.productId),
                "TabLayout": "Y",
                "TabIndex": "0",
                "ActionName": hostWindowName() || "VAS_ProductMaster",
                "ActionType": "W"
            };
            try { $self.widgetFirevalueChanged(windowParam); } catch (e) { }
            closeModal();
        }

        /* ---- Lifecycle ---- */
        this.Initalize = function () {
            build();

            if (typeof ResizeObserver !== 'undefined') {
                resizeObserver = new ResizeObserver(function () {
                    if (resizeTimer) { clearTimeout(resizeTimer); }
                    resizeTimer = setTimeout(function () {
                        if (state.mode === 'list' && state.loaded && state.items.length) {
                            renderList(state.firstIndex);
                        }
                    }, 60);
                });
                resizeObserver.observe(els.rows);
            }

            loadData();
        };

        this.refreshWidget = function () {
            closeModal();
            loadData();
        };

        this.getRoot = function () { return root; };

        this.disposeComponent = function () {
            if (loadController && typeof loadController.abort === 'function') {
                try { loadController.abort(); } catch (ignored) { }
            }
            if (resizeObserver) { resizeObserver.disconnect(); resizeObserver = null; }
            if (resizeTimer) { clearTimeout(resizeTimer); }
            if (els.overlay && els.overlay.parentNode) { els.overlay.parentNode.removeChild(els.overlay); }
            document.body.classList.remove('MPC-ir-body-lock');
            if (root.parentNode) { root.parentNode.removeChild(root); }
        };
    };

    VAS.VAS_128_IncompleteRecordsWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_128_IncompleteRecordsWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_128_IncompleteRecordsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_128_IncompleteRecordsWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_128_IncompleteRecordsWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_128_IncompleteRecordsWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
