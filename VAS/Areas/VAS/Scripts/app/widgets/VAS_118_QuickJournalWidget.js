/**
 * Quick Journal Widget (VAS_118)
 * 2x1 dashboard tile. Opens an in-place pop-up that creates a real two-line GL
 * journal (one debit + one credit) and saves it as Draft or Complete it, without
 * leaving the dashboard.
 *
 * Backend - VAS_118_QuickJournalWidget/GetInitData, GetAccounts, GetCostCenters, CreateQuickJournal
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
        var schemas = [];
        var schemasById = {};
        var currentSchema = null;
        var precision = 2;
        var currencySymbol = '';
        var currencyIso = '';
        var submitting = false;
        var acctSearchTimer = null;

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

        function populateSelect($sel, items, valKey, textKey, includeBlank) {
            /* Only optional controls (Cost center) carry a leading blank option;
               mandatory controls default to a real selected value. */
            var html = includeBlank ? '<option value=""></option>' : '';
            for (var i = 0; i < items.length; i++) {
                html += '<option value="' + escapeHtml(items[i][valKey]) + '">' + escapeHtml(items[i][textKey]) + '</option>';
            }
            $sel.html(html);
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

        /* Field icons — FontAwesome (fa) + VIS (vis) icon fonts loaded by the host
           shell. Organization / Cost center = fa-university, Journal date =
           fa-calendar, Accounting Schema = vis-open-book, Document Type =
           fa-file-text, Description = vis-file-text, Debit/Credit = vis-move-doc,
           Amount = fa-money. */
        var ICON = {
            org: '<i class="fa fa-university" aria-hidden="true"></i>',
            calendar: '<i class="fa fa-calendar" aria-hidden="true"></i>',
            book: '<i class="vis vis-open-book" aria-hidden="true"></i>',
            file: '<i class="fa fa-file-text" aria-hidden="true"></i>',
            note: '<i class="vis vis-file-text" aria-hidden="true"></i>',
            money: '<i class="vis vis-move-doc" aria-hidden="true"></i>',
            calc: '<i class="fa fa-money" aria-hidden="true"></i>',
            costcenter: '<i class="fa fa-university" aria-hidden="true"></i>'
        };

        function reqStar(required) { return required ? ' <span class="vas-qj-req">*</span>' : ''; }

        /* One field = [icon box] + (label over control), with a bottom underline. */
        function fieldWrap(iconSvg, labelHtml, controlHtml, extraClass) {
            return '<div class="vas-qj-field ' + (extraClass || '') + '">' +
                '<span class="vas-qj-field-ico">' + iconSvg + '</span>' +
                '<div class="vas-qj-field-main">' +
                '<label>' + labelHtml + '</label>' +
                controlHtml +
                '</div>' +
                '</div>';
        }

        function fieldSelect(iconKey, fieldClass, labelText, required) {
            return fieldWrap(ICON[iconKey], escapeHtml(labelText) + reqStar(required),
                '<select class="' + fieldClass + '"></select>');
        }

        function fieldCombo(iconKey, fieldClass, dotClass, labelText) {
            return fieldWrap(ICON[iconKey],
                '<span class="' + dotClass + '"></span> ' + escapeHtml(labelText) + reqStar(true),
                '<div class="vas-qj-combo ' + fieldClass + '">' +
                '<input type="text" class="vas-qj-combo-input" autocomplete="off" placeholder="' + escapeHtml(lbl("VAS_118_SearchAccount", "Search account by code or name")) + '" />' +
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
                '<div class="vas-qj-general-err vas-qj-hidden"></div>' +

                '<div class="vas-qj-row">' +
                fieldSelect('org', 'vas-qj-org', lbl("VAS_118_Organization", "Organization"), true) +
                fieldWrap(ICON.calendar, escapeHtml(lbl("VAS_118_JournalDate", "Journal date")) + reqStar(true),
                    '<input type="date" class="vas-qj-date" />') +
                '</div>' +

                '<div class="vas-qj-row">' +
                fieldSelect('book', 'vas-qj-schema', lbl("VAS_118_AccountingSchema", "Accounting Schema"), true) +
                fieldSelect('file', 'vas-qj-doctype', lbl("VAS_118_DocumentType", "Document Type"), true) +
                '</div>' +

                fieldWrap(ICON.note, escapeHtml(lbl("VAS_118_Description", "Description / narration")) + reqStar(true),
                    '<textarea class="vas-qj-desc" maxlength="255" rows="3" placeholder="' + escapeHtml(lbl("VAS_118_DescPlaceholder", "What is this entry for?")) + '"></textarea>',
                    'vas-qj-field-multiline') +

                '<div class="vas-qj-row">' +
                fieldCombo('money', 'vas-qj-debit', 'vas-qj-dr-dot', lbl("VAS_118_DebitAccount", "Debit account")) +
                fieldCombo('money', 'vas-qj-credit', 'vas-qj-cr-dot', lbl("VAS_118_CreditAccount", "Credit account")) +
                '</div>' +

                '<div class="vas-qj-row">' +
                fieldWrap(ICON.calc, escapeHtml(lbl("VAS_118_Amount", "Amount")) + reqStar(true),
                    '<div class="vas-qj-amount"><span class="vas-qj-cur"></span>' +
                    '<input type="text" class="vas-qj-amount-input" inputmode="decimal" placeholder="0.00" /></div>') +
                fieldSelect('costcenter', 'vas-qj-costcenter', lbl("VAS_118_CostCenter", "Cost center"), false) +
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
                '<button type="button" class="vas-qj-btn vas-qj-btn-primary vas-qj-post">' + escapeHtml(lbl("VAS_118_CompleteJournal", "Complete journal")) + '</button>' +
                '</div>' +
                '</div>' +

                '<div class="vas-qj-busy vas-qj-hidden"><div class="vas-qj-spinner"></div></div>' +
                '</div>' +
                '</div>'
            );

            $dialog.find('.vas-qj-scrim, .vas-qj-close, .vas-qj-cancel').on('click', closeDialog);
            $dialog.find('.vas-qj-org').on('change', onOrgChange);
            $dialog.find('.vas-qj-schema').on('change', onSchemaChange);
            $dialog.find('.vas-qj-amount-input').on('input', updateBalance);
            $dialog.find('.vas-qj-draft').on('click', function () { submit('draft'); });
            $dialog.find('.vas-qj-post').on('click', function () { submit('complete'); });

            setupCombo($dialog.find('.vas-qj-debit'));
            setupCombo($dialog.find('.vas-qj-credit'));

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
            $dialog.find('select').val('');
            $dialog.find('.vas-qj-desc, .vas-qj-amount-input').val('');
            $dialog.find('.vas-qj-combo-input').val('');
            $dialog.find('.vas-qj-combo-id').val('');
            $dialog.find('.vas-qj-combo-list').addClass('vas-qj-hidden').empty();
            $dialog.find('.vas-qj-invalid').removeClass('vas-qj-invalid');
            $dialog.find('.vas-qj-general-err').addClass('vas-qj-hidden').empty();
            currentSchema = null; precision = 2; currencySymbol = ''; currencyIso = '';
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

                    /* Mandatory selects carry NO blank option — they default to a
                       real value: Organization = the login org (when > 0), Accounting
                       Schema = the client's primary schema, Document Type = first. */
                    populateSelect($dialog.find('.vas-qj-org'), data.Organizations || [], 'Id', 'Name', false);
                    populateSelect($dialog.find('.vas-qj-schema'), schemas, 'Id', 'Name', false);
                    populateSelect($dialog.find('.vas-qj-doctype'), data.DocTypes || [], 'Id', 'Name', false);

                    if (data.DefaultOrgId > 0) { $dialog.find('.vas-qj-org').val(String(data.DefaultOrgId)); }
                    if (data.DefaultSchemaId > 0) { $dialog.find('.vas-qj-schema').val(String(data.DefaultSchemaId)); }
                    if (data.Today) { $dialog.find('.vas-qj-date').val(data.Today); }

                    /* Resolve currency + enable the debit/credit account pickers for the
                       defaulted schema (they are schema-scoped). */
                    onSchemaChange();
                    loadCostCenters();
                },
                complete: function () { showBusy(false); }
            });
        }

        function loadCostCenters() {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_118_QuickJournalWidget/GetCostCenters',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var list = parseResponse(res) || [];
                    populateSelect($dialog.find('.vas-qj-costcenter'), list, 'Id', 'Name', true);
                }
            });
        }

        function onOrgChange() {
            /* Cost centers are role/client scoped, not org-specific here, but the
               spec clears dependent selections on org change. */
            $dialog.find('.vas-qj-costcenter').val('');
            clearCombo($dialog.find('.vas-qj-debit'));
            clearCombo($dialog.find('.vas-qj-credit'));
        }

        function onSchemaChange() {
            var id = Number($dialog.find('.vas-qj-schema').val() || 0);
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

        /* ---------------- account combobox ---------------- */

        function setupCombo($combo) {
            var $input = $combo.find('.vas-qj-combo-input');
            var $id = $combo.find('.vas-qj-combo-id');
            var $list = $combo.find('.vas-qj-combo-list');

            /* Click / focus opens the list (empty term => first page of accounts). */
            $input.on('focus click', function () {
                scheduleAccountSearch($combo, $input.val());
            });
            /* Typing invalidates a prior pick and re-searches. */
            $input.on('input', function () {
                $id.val('');
                markInvalid($combo, false);
                scheduleAccountSearch($combo, $input.val());
            });

            $list.on('mousedown', '.vas-qj-combo-item', function (e) {
                e.preventDefault();
                var $item = $(this);
                $id.val($item.attr('data-id'));
                $input.val($item.attr('data-label'));
                $list.addClass('vas-qj-hidden').empty();
            });

            $input.on('blur', function () {
                setTimeout(function () { $list.addClass('vas-qj-hidden'); }, 150);
            });
        }

        function clearCombo($combo) {
            $combo.find('.vas-qj-combo-input').val('');
            $combo.find('.vas-qj-combo-id').val('');
            $combo.find('.vas-qj-combo-list').addClass('vas-qj-hidden').empty();
        }

        function scheduleAccountSearch($combo, term) {
            if (acctSearchTimer) { clearTimeout(acctSearchTimer); }
            if (!currentSchema) { return; }
            acctSearchTimer = setTimeout(function () { loadComboAccounts($combo, term); }, 220);
        }

        function loadComboAccounts($combo, term) {
            var $list = $combo.find('.vas-qj-combo-list');
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_118_QuickJournalWidget/GetAccounts',
                type: 'GET',
                cache: false,
                data: { cAcctSchemaId: currentSchema.Id, search: term || '', pageNo: 1 },
                success: function (res) {
                    var list = parseResponse(res) || [];
                    if (!list.length) { $list.addClass('vas-qj-hidden').empty(); return; }
                    var html = '';
                    for (var i = 0; i < list.length; i++) {
                        var label = list[i].Code + ' · ' + list[i].Name;
                        html += '<div class="vas-qj-combo-item" data-id="' + escapeHtml(list[i].Id) + '" data-label="' + escapeHtml(label) + '">' +
                            '<span class="vas-qj-acct-code">' + escapeHtml(list[i].Code) + '</span> ' +
                            '<span class="vas-qj-acct-name">' + escapeHtml(list[i].Name) + '</span>' +
                            '</div>';
                    }
                    $list.html(html).removeClass('vas-qj-hidden');
                }
            });
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

            req($org, Number($org.val() || 0) > 0);
            req($date, !!$date.val());
            req($schema, Number($schema.val() || 0) > 0);
            req($doctype, Number($doctype.val() || 0) > 0);
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

        function submit(action) {
            if (submitting) { return; }
            $dialog.find('.vas-qj-general-err').addClass('vas-qj-hidden').empty();
            if (!validate()) {
                showGeneralError(lbl("FillMandatory", "Please fill all mandatory fields."));
                return;
            }

            submitting = true;
            var $draft = $dialog.find('.vas-qj-draft');
            var $complete = $dialog.find('.vas-qj-post');
            $draft.prop('disabled', true);
            $complete.prop('disabled', true);
            showBusy(true);

            var amt = parseAmount($dialog.find('.vas-qj-amount-input').val());

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_118_QuickJournalWidget/CreateQuickJournal',
                type: 'POST',
                data: {
                    adOrgId: Number($dialog.find('.vas-qj-org').val() || 0),
                    dateAcct: $dialog.find('.vas-qj-date').val(),
                    cAcctSchemaId: Number($dialog.find('.vas-qj-schema').val() || 0),
                    cDocTypeId: Number($dialog.find('.vas-qj-doctype').val() || 0),
                    description: $.trim($dialog.find('.vas-qj-desc').val()),
                    debitAccountId: Number($dialog.find('.vas-qj-debit').find('.vas-qj-combo-id').val() || 0),
                    creditAccountId: Number($dialog.find('.vas-qj-credit').find('.vas-qj-combo-id').val() || 0),
                    amount: String(amt),                 /* invariant decimal string */
                    adOrgTrxId: Number($dialog.find('.vas-qj-costcenter').val() || 0),
                    action: action
                },
                success: function (res) {
                    var data = parseResponse(res);
                    if (data && data.Success) {
                        closeDialog();
                        VIS.ADialog.info ? VIS.ADialog.info("", false, data.Message, "") : alert(data.Message);
                        $self.refreshWidget();
                    } else {
                        applyFieldErrors(data && data.FieldErrors);
                        showGeneralError((data && data.Message) || lbl("VAS_118_JournalNotSaved", "The journal could not be saved."));
                    }
                },
                error: function () {
                    showGeneralError(lbl("VAS_118_JournalNotSaved", "The journal could not be saved."));
                },
                complete: function () {
                    submitting = false;
                    $draft.prop('disabled', false); $complete.prop('disabled', false);
                    showBusy(false);
                }
            });
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
