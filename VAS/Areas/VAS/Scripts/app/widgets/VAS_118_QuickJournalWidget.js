/**
 * Quick Journal Widget (VAS_118)
 * 2x1 dashboard tile. Opens an in-place pop-up that creates a real two-line GL
 * journal (one debit + one credit) and saves it as Draft or Complete it, without
 * leaving the dashboard.
 *
 * Backend - VAS_118_QuickJournalWidget/GetInitData, GetDocTypes, GetAccounts, GetCostCenters, CreateQuickJournal, CompleteQuickJournal
 *
 * Labels / Message Keys (English fallback shown; add to AD_Message)
 *  #  | Text                                             | Message Key
 * ----+--------------------------------------------------+-----------------------------------
 *  1  | Quick journal                                    | VAS_118_QuickJournal
 *  2  | 2-line entry - stay on this screen               | VAS_118_TileSubtitle
 *  3  | Quick Journal Entry                              | VAS_118_ModalTitle
 *  4  | Single debit / single credit - auto-balanced     | VAS_118_ModalSubtitle
 *  5  | Organization                                     | VAS_118_Organization
 *  6  | Journal date                                     | VAS_118_JournalDate
 *  7  | Accounting Schema                                | VAS_118_AccountingSchema
 *  8  | Document Type                                    | VAS_118_DocumentType
 *  9  | Description / narration                          | VAS_118_Description
 * 10  | Debit account                                    | VAS_118_DebitAccount
 * 11  | Credit account                                   | VAS_118_CreditAccount
 * 12  | Amount                                           | VAS_118_Amount
 * 13  | Cost center                                      | VAS_118_CostCenter
 * 14  | Debit                                            | VAS_118_Debit
 * 15  | Credit                                           | VAS_118_Credit
 * 16  | Balanced                                         | VAS_118_Balanced
 * 17  | Cancel                                           | VAS_118_Cancel
 * 18  | Save draft                                       | VAS_118_SaveDraft
 * 19  | Complete journal                                 | VAS_118_CompleteJournal
 * 20  | Select...                                        | VAS_118_SelectPlaceholder
 * 21  | Search account by code or name                   | VAS_118_SearchAccount
 * 22  | None                                             | VAS_118_None
 * 23  | What is this entry for?                          | VAS_118_DescPlaceholder
 * 24  | Close                                            | VAS_118_Close
 * 25  | The journal could not be saved.                  | VAS_118_JournalNotSaved
 * 26  | Journal {0} saved as draft.                      | VAS_118_JournalSavedDraft
 * 27  | Journal {0} Complete successfully.               | VAS_118_JournalComplete
 * 28  | Search organization                              | VAS_118_SearchOrg
 * 29  | Search accounting schema                         | VAS_118_SearchSchema
 * 30  | Search document type                             | VAS_118_SearchDocType
 * 31  | Search cost center                               | VAS_118_SearchCostCenter
 * 32  | Draft                                            | VAS_118_Draft
 * 33  | Completed                                        | VAS_118_Completed
 * 34  | Closed                                           | VAS_118_Closed
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    function ensureDashInlineSizeVar($el) {
        if (window.__vasDashInlineSizeObserver) { return; }
        if (typeof ResizeObserver === 'undefined') { return; }

        var container = $el.closest('.vis-widget-container, [data-dashboard-container]')[0];
        if (!container) { return; }

        var write = function () {
            document.documentElement.style.setProperty('--dash-inline-size', container.clientWidth + 'px');
        };

        window.__vasDashInlineSizeObserver = new ResizeObserver(write);
        window.__vasDashInlineSizeObserver.observe(container);
        write();
    }

    VAS.VAS_118_QuickJournalWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-qj-root">');
        var $dialog = null;

        /* Lookup state */
        var orgs = [];
        var docTypes = [];
        var costCenters = [];
        var schemas = [];
        var schemasById = {};
        var currentSchema = null;
        var precision = 2;
        var currencySymbol = '';
        var currencyIso = '';
        var submitting = false;
        var acctSearchTimer = null;
        var currentJournalId = 0;   /* > 0 once saved as draft; the modal stays open on it */
        var savedSnapshot = '';     /* JSON of the field values at the last save (dirty check) */

        /* ---------------- helpers ---------------- */

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function escapeHtml(value) {
            return String(value == null ? "" : value)
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;")
                .replace(/'/g, "&#039;");
        }

        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data || {};
        }

        /* Locale-aware decimal parse (comma vs dot) per VIS.Env.isDecimalPoint(). */
        function parseAmount(raw) {
            var s = String(raw == null ? "" : raw).trim();
            if (!s) { return 0; }
            var usesPoint = true;
            try { usesPoint = VIS.Env.isDecimalPoint(); } catch (e) { usesPoint = true; }
            if (usesPoint) {
                s = s.replace(/,/g, '');                       /* thousands sep */
            } else {
                s = s.replace(/\./g, '').replace(/,/g, '.');   /* comma decimal */
            }
            s = s.replace(/[^0-9.\-]/g, '');
            var n = parseFloat(s);
            return isNaN(n) ? 0 : n;
        }

        function formatAmount(num) {
            var n = Number(num || 0);
            if (isNaN(n)) { n = 0; }
            var text = n.toLocaleString(window.navigator.language, {
                minimumFractionDigits: precision,
                maximumFractionDigits: precision
            });
            if (!currencySymbol) { return text; }
            /* 3-char ISO reads better after the amount; a glyph symbol before it. */
            return currencySymbol.length === 3 ? (text + ' ' + currencySymbol) : (currencySymbol + text);
        }

        function markInvalid($el, on) {
            /* The invalid state lives on the field wrapper (red underline + icon). */
            $el.closest('.vas-qj-field').toggleClass('vas-qj-invalid', !!on);
        }

        /* ---------------- widget tile ---------------- */

        function createWidget() {
            var $card = $(
                '<button type="button" class="vas-qj-card" aria-label="' + escapeHtml(lbl("VAS_118_QuickJournal", "Quick journal")) + '">' +
                '<div class="vas-qj-card-head">' +
                '<span class="vas-qj-ico">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M13 2L3 14h9l-1 8 10-12h-9l1-8z"/></svg>' +
                '</span>' +
                '</div>' +
                '<div class="vas-qj-card-text">' +
                '<div class="vas-qj-card-title">' + escapeHtml(lbl("VAS_118_QuickJournal", "Quick journal")) + '</div>' +
                '<div class="vas-qj-card-sub">' + escapeHtml(lbl("VAS_118_TileSubtitle", "2-line entry · stay on this screen")) + '</div>' +
                '</div>' +
                '</button>'
            );
            $card.on('click', openDialog);
            $root.append($card);
        }

        /* ---------------- modal ---------------- */

        /* Field icon CLASSES — FontAwesome (fa) + VIS (vis) icon fonts loaded by the
           host shell, rendered as a plain inline <i> inside the control (no box),
           the VAS_065_APInvoicePanel pattern. */
        var ICON = {
            org: 'fa fa-university',
            calendar: 'fa fa-calendar',
            book: 'vis vis-open-book',
            file: 'fa fa-file-text',
            note: 'vis vis-file-text',
            money: 'vis vis-move-doc',
            calc: 'fa fa-money',
            costcenter: 'fa fa-university'
        };

        function reqStar(required) { return required ? ' <span class="vas-qj-req">*</span>' : ''; }

        /* One field (VAS_065 style) = label on top, then a .vas-qj-control row that
           holds a plain <i> icon before the input, with a bottom underline. */
        function fieldWrap(iconCls, labelHtml, controlHtml, extraClass) {
            return '<div class="vas-qj-field ' + (extraClass || '') + '">' +
                '<label>' + labelHtml + '</label>' +
                '<div class="vas-qj-control">' +
                '<i class="vas-qj-field-ic ' + iconCls + '" aria-hidden="true"></i>' +
                controlHtml +
                '</div>' +
                '</div>';
        }

        /* Every picker (organization, schema, document type, cost center, debit,
           credit) is the same type-to-filter combobox. labelHtml is pre-built by the
           caller (it may include a debit/credit colour dot). */
        function fieldCombo(iconKey, fieldClass, labelHtml, placeholder, required) {
            return fieldWrap(ICON[iconKey], labelHtml + reqStar(required),
                '<div class="vas-qj-combo ' + fieldClass + '">' +
                '<input type="text" class="vas-qj-combo-input" autocomplete="off" placeholder="' + escapeHtml(placeholder) + '" />' +
                '<input type="hidden" class="vas-qj-combo-id" value="" />' +
                '<div class="vas-qj-combo-list vas-qj-hidden"></div>' +
                '</div>');
        }

        function createDialog() {
            $dialog = $(
                '<div class="vas-qj-dialog vas-qj-hidden" role="dialog" aria-modal="true" aria-labelledby="vas-qj-title">' +
                '<div class="vas-qj-scrim"></div>' +
                '<div class="vas-qj-modal">' +
                '<div class="vas-qj-header">' +
                '<div class="vas-qj-modal-ico">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M13 2L3 14h9l-1 8 10-12h-9l1-8z"/></svg>' +
                '</div>' +
                '<div class="vas-qj-header-text">' +
                '<div class="vas-qj-modal-title" id="vas-qj-title">' + escapeHtml(lbl("VAS_118_ModalTitle", "Quick Journal Entry")) + '</div>' +
                '<div class="vas-qj-modal-sub">' + escapeHtml(lbl("VAS_118_ModalSubtitle", "Single debit / single credit · auto-balanced · stays on this dashboard")) + '</div>' +
                '</div>' +
                '<button type="button" class="vas-qj-close" aria-label="' + escapeHtml(lbl("VAS_118_Close", "Close")) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M18 6L6 18M6 6l12 12"/></svg>' +
                '</button>' +
                '</div>' +

                '<div class="vas-qj-body">' +
                '<div class="vas-qj-status vas-qj-hidden"></div>' +
                '<div class="vas-qj-general-err vas-qj-hidden"></div>' +

                '<div class="vas-qj-row">' +
                fieldCombo('org', 'vas-qj-org', escapeHtml(lbl("VAS_118_Organization", "Organization")), lbl("VAS_118_SearchOrg", "Search organization"), true) +
                fieldWrap(ICON.calendar, escapeHtml(lbl("VAS_118_JournalDate", "Journal date")) + reqStar(true),
                    '<input type="date" class="vas-qj-date" />') +
                '</div>' +

                '<div class="vas-qj-row">' +
                fieldCombo('book', 'vas-qj-schema', escapeHtml(lbl("VAS_118_AccountingSchema", "Accounting Schema")), lbl("VAS_118_SearchSchema", "Search accounting schema"), true) +
                fieldCombo('file', 'vas-qj-doctype', escapeHtml(lbl("VAS_118_DocumentType", "Document Type")), lbl("VAS_118_SearchDocType", "Search document type"), true) +
                '</div>' +

                fieldWrap(ICON.note, escapeHtml(lbl("VAS_118_Description", "Description / narration")) + reqStar(true),
                    '<textarea class="vas-qj-desc" maxlength="255" rows="3" placeholder="' + escapeHtml(lbl("VAS_118_DescPlaceholder", "What is this entry for?")) + '"></textarea>',
                    'vas-qj-field-multiline') +

                '<div class="vas-qj-row">' +
                fieldCombo('money', 'vas-qj-debit', '<span class="vas-qj-dr-dot"></span> ' + escapeHtml(lbl("VAS_118_DebitAccount", "Debit account")), lbl("VAS_118_SearchAccount", "Search account by code or name"), true) +
                fieldCombo('money', 'vas-qj-credit', '<span class="vas-qj-cr-dot"></span> ' + escapeHtml(lbl("VAS_118_CreditAccount", "Credit account")), lbl("VAS_118_SearchAccount", "Search account by code or name"), true) +
                '</div>' +

                '<div class="vas-qj-row">' +
                fieldWrap(ICON.calc, escapeHtml(lbl("VAS_118_Amount", "Amount")) + reqStar(true),
                    '<span class="vas-qj-cur"></span>' +
                    '<input type="text" class="vas-qj-amount-input" inputmode="decimal" placeholder="0.00" />') +
                fieldCombo('costcenter', 'vas-qj-costcenter', escapeHtml(lbl("VAS_118_CostCenter", "Cost center")), lbl("VAS_118_SearchCostCenter", "Search cost center"), false) +
                '</div>' +

                '<div class="vas-qj-preview">' +
                '<div class="vas-qj-leg"><span>' + escapeHtml(lbl("VAS_118_Debit", "Debit")) + '</span><strong class="vas-qj-dr">' + escapeHtml(formatAmount(0)) + '</strong></div>' +
                '<div class="vas-qj-leg"><span>' + escapeHtml(lbl("VAS_118_Credit", "Credit")) + '</span><strong class="vas-qj-cr">' + escapeHtml(formatAmount(0)) + '</strong></div>' +
                '<div class="vas-qj-balanced">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg> ' +
                escapeHtml(lbl("VAS_118_Balanced", "Balanced")) +
                '</div>' +
                '</div>' +
                '</div>' +

                '<div class="vas-qj-foot">' +
                '<button type="button" class="vas-qj-btn vas-qj-btn-ghost vas-qj-cancel">' + escapeHtml(lbl("VAS_118_Cancel", "Cancel")) + '</button>' +
                '<div class="vas-qj-foot-actions">' +
                '<button type="button" class="vas-qj-btn vas-qj-btn-secondary vas-qj-draft">' + escapeHtml(lbl("VAS_118_SaveDraft", "Save draft")) + '</button>' +
                '<button type="button" class="vas-qj-btn vas-qj-btn-primary vas-qj-post" disabled>' + escapeHtml(lbl("VAS_118_CompleteJournal", "Complete journal")) + '</button>' +
                '</div>' +
                '</div>' +

                '<div class="vas-qj-busy vas-qj-hidden"><div class="vas-qj-spinner"></div></div>' +
                '</div>' +
                '</div>'
            );

            /* Do NOT close on scrim (outside) click — only the X and Cancel close it. */
            $dialog.find('.vas-qj-close, .vas-qj-cancel').on('click', closeDialog);
            $dialog.find('.vas-qj-amount-input').on('input', updateBalance);
            $dialog.find('.vas-qj-draft').on('click', onSaveDraft);
            $dialog.find('.vas-qj-post').on('click', onComplete);

            /* Type-to-filter comboboxes. Organization / Schema / Document Type / Cost
               center filter a pre-loaded list client-side; Debit / Credit search the
               schema's accounts on the server. */
            setupCombo($dialog.find('.vas-qj-org'), staticProvider(function () { return orgs; }), onOrgChange);
            setupCombo($dialog.find('.vas-qj-schema'), staticProvider(function () { return schemas; }), onSchemaChange);
            setupCombo($dialog.find('.vas-qj-doctype'), staticProvider(function () { return docTypes; }), null);
            setupCombo($dialog.find('.vas-qj-costcenter'), staticProvider(function () { return costCenters; }, true), null);
            setupCombo($dialog.find('.vas-qj-debit'), accountProvider, null);
            setupCombo($dialog.find('.vas-qj-credit'), accountProvider, null);

            /* Native date control (VAS_065 pattern): the native right-side indicator
               is hidden in CSS; clicking anywhere on the field opens the picker.
               showPicker() must run from a user gesture. */
            $dialog.find('.vas-qj-control').each(function () {
                var $ctrl = $(this);
                var dateInput = $ctrl.find('input[type="date"]')[0];
                if (dateInput) {
                    $ctrl.on('click', function () {
                        if (typeof dateInput.showPicker === 'function') {
                            try { dateInput.showPicker(); } catch (e) { /* unsupported */ }
                        }
                    });
                }
            });

            $('body').append($dialog);
        }

        function openDialog() {
            resetForm();
            $dialog.removeClass('vas-qj-hidden');
            $('body').addClass('vas-qj-body-lock');
            $(document).on('keydown.vas-qj', function (e) { if (e.key === 'Escape') { closeDialog(); } });
            loadInitData();
            setTimeout(function () { $dialog.find('.vas-qj-desc').focus(); }, 150);
        }

        function closeDialog() {
            $dialog.addClass('vas-qj-hidden');
            $('body').removeClass('vas-qj-body-lock');
            $(document).off('keydown.vas-qj');
        }

        function resetForm() {
            $dialog.find('.vas-qj-desc, .vas-qj-amount-input').val('');
            $dialog.find('.vas-qj-combo-input').val('');
            $dialog.find('.vas-qj-combo-id').val('');
            $dialog.find('.vas-qj-combo-list').addClass('vas-qj-hidden').empty();
            $dialog.find('.vas-qj-invalid').removeClass('vas-qj-invalid');
            $dialog.find('.vas-qj-general-err').addClass('vas-qj-hidden').empty();
            $dialog.find('.vas-qj-status').addClass('vas-qj-hidden').empty();
            currentSchema = null; precision = 2; currencySymbol = ''; currencyIso = '';
            currentJournalId = 0; savedSnapshot = '';
            /* Fresh entry: Complete is disabled until the draft is saved. */
            $dialog.find('.vas-qj-draft').prop('disabled', false);
            $dialog.find('.vas-qj-post').prop('disabled', true);
            $dialog.find('.vas-qj-cur').text('');
            updateBalance();
        }

        function showBusy(on) {
            $dialog.find('.vas-qj-busy').toggleClass('vas-qj-hidden', !on);
        }

        /* ---------------- data loading ---------------- */

        function loadInitData() {
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_118_QuickJournalWidget/GetInitData',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    schemas = data.Schemas || [];
                    schemasById = {};
                    for (var i = 0; i < schemas.length; i++) { schemasById[schemas[i].Id] = schemas[i]; }
                    orgs = data.Organizations || [];
                    docTypes = data.DocTypes || [];

                    /* Pre-fill the type-to-filter combos with real defaults: Organization
                       = login org (when > 0), Accounting Schema = primary schema,
                       Document Type = first. Cost center stays blank (optional). */
                    presetCombo('.vas-qj-org', findById(orgs, data.DefaultOrgId));
                    presetCombo('.vas-qj-schema', findById(schemas, data.DefaultSchemaId));
                    presetCombo('.vas-qj-doctype', docTypes.length ? docTypes[0] : null);
                    if (data.Today) { $dialog.find('.vas-qj-date').val(data.Today); }

                    /* Resolve currency + enable the debit/credit account pickers for the
                       defaulted schema (they are schema-scoped). Document types arrive
                       already org-scoped for the default org; cost centers load for it. */
                    onSchemaChange();
                    loadCostCenters(data.DefaultOrgId);
                },
                complete: function () { showBusy(false); }
            });
        }

        /* Selected header organization id (0 when none). */
        function selectedOrgId() { return Number($dialog.find('.vas-qj-org .vas-qj-combo-id').val() || 0); }

        function loadCostCenters(orgId) {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_118_QuickJournalWidget/GetCostCenters',
                type: 'GET',
                cache: false,
                data: { cAdOrgId: orgId || 0 },
                success: function (res) { costCenters = parseResponse(res) || []; }
            });
        }

        /* Reloads the GL-Journal document types for an organization; optionally
           re-selects the first one. */
        function loadDocTypes(orgId, presetFirst) {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_118_QuickJournalWidget/GetDocTypes',
                type: 'GET',
                cache: false,
                data: { cAdOrgId: orgId || 0 },
                success: function (res) {
                    docTypes = parseResponse(res) || [];
                    if (presetFirst) { presetCombo('.vas-qj-doctype', docTypes.length ? docTypes[0] : null); }
                }
            });
        }

        /* Finds a { Id, Name } item by id in a loaded list. */
        function findById(list, id) {
            for (var i = 0; i < (list || []).length; i++) {
                if (Number(list[i].Id) === Number(id)) { return list[i]; }
            }
            return null;
        }

        /* Presets a combo to a { Id, Name } item (or clears it when null). */
        function presetCombo(sel, obj) {
            var $c = $dialog.find(sel);
            if (obj) {
                $c.find('.vas-qj-combo-input').val(obj.Name);
                $c.find('.vas-qj-combo-id').val(obj.Id);
            } else {
                clearCombo($c);
            }
        }

        function onOrgChange() {
            /* Document type, cost center and account are all org-scoped — reload for
               the new organization and clear the now-stale dependent picks. */
            var orgId = selectedOrgId();
            clearCombo($dialog.find('.vas-qj-doctype'));
            clearCombo($dialog.find('.vas-qj-costcenter'));
            clearCombo($dialog.find('.vas-qj-debit'));
            clearCombo($dialog.find('.vas-qj-credit'));
            loadDocTypes(orgId, true);
            loadCostCenters(orgId);
        }

        function onSchemaChange() {
            var id = Number($dialog.find('.vas-qj-schema .vas-qj-combo-id').val() || 0);
            currentSchema = schemasById[id] || null;
            precision = currentSchema ? Number(currentSchema.Precision || 2) : 2;
            currencySymbol = currentSchema ? (currentSchema.CurrencySymbol || '') : '';
            currencyIso = currentSchema ? (currentSchema.CurrencyIso || '') : '';
            $dialog.find('.vas-qj-cur').text(currencySymbol || currencyIso);
            /* Accounts depend on the schema - clear stale picks. */
            clearCombo($dialog.find('.vas-qj-debit'));
            clearCombo($dialog.find('.vas-qj-credit'));
            updateBalance();
        }

        /* ---------------- combobox (type-to-filter) ---------------- */

        /* Wires a combo to a provider(term, cb) that yields [{ id, label, html? }].
           onSelect (optional) fires after the user picks an item. */
        function setupCombo($combo, provider, onSelect) {
            var $input = $combo.find('.vas-qj-combo-input');
            var $id = $combo.find('.vas-qj-combo-id');
            var $list = $combo.find('.vas-qj-combo-list');

            function render(items) {
                if (!items || !items.length) { $list.addClass('vas-qj-hidden').empty(); return; }
                var html = '';
                for (var i = 0; i < items.length; i++) {
                    html += '<div class="vas-qj-combo-item" data-id="' + escapeHtml(items[i].id) + '" data-label="' + escapeHtml(items[i].label) + '">' +
                        (items[i].html || escapeHtml(items[i].label)) + '</div>';
                }
                $list.html(html).removeClass('vas-qj-hidden');
                positionList();
            }

            /* Open the list upward when there isn't room below inside the modal — a
               downward list on the last row (Cost center) overflows the modal and
               triggers its scrollbar. */
            function positionList() {
                var inputEl = $input[0], listEl = $list[0];
                if (!inputEl || !listEl) { return; }
                var $modal = $combo.closest('.vas-qj-modal');
                var boundBottom = $modal.length ? $modal[0].getBoundingClientRect().bottom : window.innerHeight;
                var spaceBelow = boundBottom - inputEl.getBoundingClientRect().bottom;
                $list.toggleClass('vas-qj-combo-list-up', spaceBelow < listEl.offsetHeight + 8);
            }

            /* Click / focus opens the list (empty term => all / first page). */
            $input.on('focus click', function () { provider($input.val(), render); });
            /* Typing invalidates a prior pick and re-filters. */
            $input.on('input', function () { $id.val(''); markInvalid($combo, false); provider($input.val(), render); });

            $list.on('mousedown', '.vas-qj-combo-item', function (e) {
                e.preventDefault();
                var $item = $(this);
                var id = $item.attr('data-id') || '';
                $id.val(id);
                /* The "None" row (empty id) clears the field. */
                $input.val(id ? $item.attr('data-label') : '');
                $list.addClass('vas-qj-hidden').empty();
                if (onSelect) { onSelect(); }
            });

            $input.on('blur', function () {
                setTimeout(function () {
                    $list.addClass('vas-qj-hidden');
                    /* Typed text that was never resolved to a real pick is discarded so
                       a filled-looking input never carries an unselected value. */
                    if (!$id.val()) { $input.val(''); }
                }, 150);
            });
        }

        function clearCombo($combo) {
            $combo.find('.vas-qj-combo-input').val('');
            $combo.find('.vas-qj-combo-id').val('');
            $combo.find('.vas-qj-combo-list').addClass('vas-qj-hidden').empty();
        }

        /* Client-side filter over a pre-loaded list of { Id, Name }. allowEmpty
           prepends a "None" row (empty id) so an optional field can be cleared. */
        function staticProvider(getRaw, allowEmpty) {
            return function (term, cb) {
                var t = $.trim(term || '').toLowerCase();
                var raw = getRaw() || [];
                var out = [];
                if (allowEmpty && !t) {
                    out.push({ id: '', label: '', html: '<span class="vas-qj-none"></span>' });
                }
                for (var i = 0; i < raw.length; i++) {
                    var label = String(raw[i].Name == null ? '' : raw[i].Name);
                    if (!t || label.toLowerCase().indexOf(t) >= 0) { out.push({ id: raw[i].Id, label: label }); }
                }
                cb(out);
            };
        }

        /* Server-side account search (debounced), scoped to the chosen schema. */
        function accountProvider(term, cb) {
            if (!currentSchema) { cb([]); return; }
            if (acctSearchTimer) { clearTimeout(acctSearchTimer); }
            acctSearchTimer = setTimeout(function () {
                $.ajax({
                    url: VIS.Application.contextUrl + 'VAS_118_QuickJournalWidget/GetAccounts',
                    type: 'GET',
                    cache: false,
                    data: { cAcctSchemaId: currentSchema.Id, search: term || '', pageNo: 1, adOrgId: selectedOrgId() },
                    success: function (res) {
                        var list = parseResponse(res) || [];
                        var out = [];
                        for (var i = 0; i < list.length; i++) {
                            out.push({
                                id: list[i].Id,
                                label: list[i].Code + ' · ' + list[i].Name,
                                html: '<span class="vas-qj-acct-code">' + escapeHtml(list[i].Code) + '</span> <span class="vas-qj-acct-name">' + escapeHtml(list[i].Name) + '</span>'
                            });
                        }
                        cb(out);
                    },
                    error: function () { cb([]); }
                });
            }, 220);
        }

        /* ---------------- balance preview ---------------- */

        function updateBalance() {
            var amt = parseAmount($dialog.find('.vas-qj-amount-input').val());
            var text = formatAmount(amt);
            $dialog.find('.vas-qj-dr').text(text);
            $dialog.find('.vas-qj-cr').text(text);
        }

        /* ---------------- submit ---------------- */

        function validate() {
            var ok = true;
            function req($f, cond) { markInvalid($f, !cond); if (!cond) { ok = false; } }

            var $org = $dialog.find('.vas-qj-org');
            var $date = $dialog.find('.vas-qj-date');
            var $schema = $dialog.find('.vas-qj-schema');
            var $doctype = $dialog.find('.vas-qj-doctype');
            var $desc = $dialog.find('.vas-qj-desc');
            var $debit = $dialog.find('.vas-qj-debit');
            var $credit = $dialog.find('.vas-qj-credit');
            var $amount = $dialog.find('.vas-qj-amount-input');

            req($org, Number($org.find('.vas-qj-combo-id').val() || 0) > 0);
            req($date, !!$date.val());
            req($schema, Number($schema.find('.vas-qj-combo-id').val() || 0) > 0);
            req($doctype, Number($doctype.find('.vas-qj-combo-id').val() || 0) > 0);
            req($desc, !!$.trim($desc.val()));
            var debitId = Number($debit.find('.vas-qj-combo-id').val() || 0);
            var creditId = Number($credit.find('.vas-qj-combo-id').val() || 0);
            req($debit, debitId > 0);
            req($credit, creditId > 0);
            req($amount, parseAmount($amount.val()) > 0);
            if (debitId > 0 && debitId === creditId) { markInvalid($credit, true); ok = false; }
            return ok;
        }

        function showGeneralError(msg) {
            $dialog.find('.vas-qj-general-err').text(msg || '').removeClass('vas-qj-hidden');
        }

        /* Current field values — the create/update payload and the dirty snapshot. */
        function collectValues() {
            return {
                adOrgId: Number($dialog.find('.vas-qj-org .vas-qj-combo-id').val() || 0),
                dateAcct: $dialog.find('.vas-qj-date').val(),
                cAcctSchemaId: Number($dialog.find('.vas-qj-schema .vas-qj-combo-id').val() || 0),
                cDocTypeId: Number($dialog.find('.vas-qj-doctype .vas-qj-combo-id').val() || 0),
                description: $.trim($dialog.find('.vas-qj-desc').val()),
                debitAccountId: Number($dialog.find('.vas-qj-debit .vas-qj-combo-id').val() || 0),
                creditAccountId: Number($dialog.find('.vas-qj-credit .vas-qj-combo-id').val() || 0),
                amount: String(parseAmount($dialog.find('.vas-qj-amount-input').val())),
                adOrgTrxId: Number($dialog.find('.vas-qj-costcenter .vas-qj-combo-id').val() || 0)
            };
        }

        function isDirty() { return JSON.stringify(collectValues()) !== savedSnapshot; }

        /* Disable both buttons while a request is in flight; when idle, Complete is
           enabled only once a draft exists. */
        function setBusy(on) {
            submitting = on;
            $dialog.find('.vas-qj-draft').prop('disabled', on);
            $dialog.find('.vas-qj-post').prop('disabled', on || currentJournalId <= 0);
            showBusy(on);
        }

        /* Draft / status strip: "Draft · GJ-000123". */
        function showStatus(docStatus, docNo) {
            var label = docStatus === 'CO' ? lbl("VAS_118_Completed", "Completed")
                : docStatus === 'CL' ? lbl("VAS_118_Closed", "Closed")
                    : lbl("VAS_118_Draft", "Draft");
            $dialog.find('.vas-qj-status')
                .text(label + (docNo ? ' · ' + docNo : ''))
                .removeClass('vas-qj-hidden');
        }

        /* Creates the draft (currentJournalId == 0) or updates it. cb(ok, data). */
        function postSave(cb) {
            var values = collectValues();
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_118_QuickJournalWidget/CreateQuickJournal',
                type: 'POST',
                data: $.extend({ glJournalId: currentJournalId }, values),
                success: function (res) {
                    var data = parseResponse(res);
                    if (data && data.Success) {
                        currentJournalId = Number(data.GL_Journal_ID || 0);
                        savedSnapshot = JSON.stringify(values);
                        showStatus(data.DocStatus, data.DocumentNo);
                        cb(true, data);
                    } else {
                        applyFieldErrors(data && data.FieldErrors);
                        showGeneralError((data && data.Message) || lbl("VAS_118_JournalNotSaved", "The journal could not be saved."));
                        cb(false, data);
                    }
                },
                error: function () {
                    showGeneralError(lbl("VAS_118_JournalNotSaved", "The journal could not be saved."));
                    cb(false);
                }
            });
        }

        /* Completes the kept draft, then closes the modal. cb() always fires. */
        function postComplete(cb) {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_118_QuickJournalWidget/CompleteQuickJournal',
                type: 'POST',
                data: { glJournalId: currentJournalId },
                success: function (res) {
                    var data = parseResponse(res);
                    if (data && data.Success) {
                        closeDialog();
                        if (VIS.ADialog && VIS.ADialog.info) { VIS.ADialog.info("", false, data.Message, ""); }
                        $self.refreshWidget();
                    } else {
                        showStatus(data && data.DocStatus, data && data.DocumentNo);
                        showGeneralError((data && data.Message) || lbl("VAS_118_JournalNotCompleted", "The journal could not be completed."));
                    }
                    cb();
                },
                error: function () {
                    showGeneralError(lbl("VAS_118_JournalNotCompleted", "The journal could not be completed."));
                    cb();
                }
            });
        }

        /* Save draft: creates on the first click, updates the draft afterwards, and
           keeps the modal open with the document number + Complete enabled. */
        function onSaveDraft() {
            if (submitting) { return; }
            $dialog.find('.vas-qj-general-err').addClass('vas-qj-hidden').empty();
            if (!validate()) { showGeneralError(lbl("FillMandatory", "Please fill all mandatory fields.")); return; }
            setBusy(true);
            postSave(function (ok, data) {
                setBusy(false);
                if (ok && VIS.ADialog && VIS.ADialog.info) { VIS.ADialog.info("", false, data.Message, ""); }
            });
        }

        /* Complete: if the user changed anything since the last save, persist the
           edits first, then complete; otherwise complete directly. */
        function onComplete() {
            if (submitting || currentJournalId <= 0) { return; }
            $dialog.find('.vas-qj-general-err').addClass('vas-qj-hidden').empty();
            if (!validate()) { showGeneralError(lbl("FillMandatory", "Please fill all mandatory fields.")); return; }
            setBusy(true);
            if (isDirty()) {
                postSave(function (ok) {
                    if (!ok) { setBusy(false); return; }
                    postComplete(function () { setBusy(false); });
                });
            } else {
                postComplete(function () { setBusy(false); });
            }
        }

        function applyFieldErrors(fieldErrors) {
            if (!fieldErrors) { return; }
            var map = {
                'AD_Org_ID': '.vas-qj-org',
                'DateAcct': '.vas-qj-date',
                'C_AcctSchema_ID': '.vas-qj-schema',
                'C_DocType_ID': '.vas-qj-doctype',
                'Description': '.vas-qj-desc',
                'DebitAccount_ID': '.vas-qj-debit',
                'CreditAccount_ID': '.vas-qj-credit',
                'Amount': '.vas-qj-amount-input',
                'AD_OrgTrx_ID': '.vas-qj-costcenter'
            };
            for (var key in fieldErrors) {
                if (fieldErrors.hasOwnProperty(key) && map[key]) {
                    markInvalid($dialog.find(map[key]), true);
                }
            }
        }

        /* ---------------- lifecycle ---------------- */

        this.Initalize = function () {
            createWidget();
            createDialog();
        };

        this.refreshWidget = function () {
            submitting = false;
            currentSchema = null;
            if ($dialog) { resetForm(); }
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $(document).off('keydown.vas-qj');
            $('body').removeClass('vas-qj-body-lock');
            if (acctSearchTimer) { clearTimeout(acctSearchTimer); }
            if ($dialog) { $dialog.off(); $dialog.remove(); $dialog = null; }
            $root.off();
            $root.remove();
        };
    };

    VAS.VAS_118_QuickJournalWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_118_QuickJournalWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_118_QuickJournalWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_118_QuickJournalWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_118_QuickJournalWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_118_QuickJournalWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
