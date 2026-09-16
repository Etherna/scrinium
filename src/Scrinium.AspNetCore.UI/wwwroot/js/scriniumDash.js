(function () {
    'use strict';

    var POLL_IDLE_MS = 3000;
    var POLL_ACTIVE_MS = 1000;
    var FEEDBACK_TIMEOUT_MS = 5000;

    var baseUrl = window.location.pathname;
    //antiforgery token rendered by the page, validated by the server on every post
    var antiforgeryToken = document.querySelector('input[name="__RequestVerificationToken"]').value;
    var banner = document.getElementById('connection-banner');
    var allCards = Array.prototype.slice.call(document.querySelectorAll('.dbcontext-card'));
    //read-only db contexts render as static cards, with no migration controls to drive
    var cards = allCards.filter(function (card) {
        return card.dataset.readOnly !== 'true';
    });
    var pollTimer = null;

    //model schemas are available on every db context, read-only ones included
    allCards.forEach(function (card) {
        var section = card.querySelector('[data-role="schemas"]');
        section.addEventListener('toggle', function () {
            /* Size the collections lazily: it reads their metadata, a constant cost.
             * The schema ids count scans a whole collection, and stays on demand. */
            if (section.open && !section.dataset.loaded)
                loadCollectionSizes(card);
        });

        Array.prototype.forEach.call(card.querySelectorAll('.schema-collection'), function (collection) {
            collection.querySelector('[data-role="count-schemas"]').addEventListener('click', function () {
                loadSchemaCounts(card, collection);
            });
        });

        /* Missing origin references are available on every db context too: the scan is a
         * read, and the repair control renders only on the writable repositories. */
        Array.prototype.forEach.call(card.querySelectorAll('.missing-origin-collection'), function (collection) {
            collection.querySelector('[data-role="scan-references"]').addEventListener('click', function () {
                scanMissingOriginReferences(card, collection);
            });

            var repairButton = collection.querySelector('[data-role="repair-references"]');
            if (repairButton) {
                repairButton.addEventListener('click', function () {
                    startReferencesRepair(card, collection, false);
                });
                collection.querySelector('[data-role="repair-references-dry-run"]').addEventListener('click', function () {
                    startReferencesRepair(card, collection, true);
                });
            }
        });
    });

    cards.forEach(function (card) {
        card.querySelector('[data-role="start"]').addEventListener('click', function () {
            startMigration(card, false);
        });
        card.querySelector('[data-role="start-dry-run"]').addEventListener('click', function () {
            startMigration(card, true);
        });
        card.querySelector('[data-role="expand-history"]').addEventListener('click', function () {
            toggleHistoryExpansion(card);
        });
    });

    if (cards.length !== 0)
        refreshStatus();

    function startMigration(card, dryRun) {
        var identifier = card.dataset.identifier;
        var stopAtFirstError = card.querySelector('[data-role="stop-at-first-error"]').checked;
        var rewriteDeprecatedSchemas = card.querySelector('[data-role="rewrite-deprecated-schemas"]').checked;
        //the lease duration is validated server side too: the control only bounds the ordinary case
        var lockLeaseDurationMinutes = card.querySelector('[data-role="lock-lease-duration"]').value;
        //a start migrates what the application declares, and nothing else: say it before it runs
        var scope = rewriteDeprecatedSchemas
            ? 'It runs the document migrations the application declares, and rewrites every document ' +
              'left on a deprecated schema, on every writable collection.'
            : 'It runs the document migrations the application declares, and nothing else.';
        var message = dryRun
            ? 'Start migration dry run on "' + identifier + '"?\n\n' + scope +
              '\n\nThe dry run simulates them without persisting anything, reporting the failing ' +
              'documents, and skips the index steps. Data stays accessible while it runs.'
            : 'Start migration on "' + identifier + '"?\n\n' + scope +
              '\n\nWhile the migration is running, the db context denies concurrent access to data.';
        message += stopAtFirstError
            ? '\n\nIt stops at the first failing document.'
            : '\n\nFailing documents are skipped and reported, without stopping the scan.';
        if (!window.confirm(message))
            return;

        card.querySelector('[data-role="start"]').disabled = true;
        card.querySelector('[data-role="start-dry-run"]').disabled = true;

        fetch(baseUrl + '?handler=StartMigration', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded',
                'RequestVerificationToken': antiforgeryToken
            },
            body: new URLSearchParams({
                identifier: identifier,
                dryRun: dryRun,
                stopAtFirstError: stopAtFirstError,
                rewriteDeprecatedSchemas: rewriteDeprecatedSchemas,
                lockLeaseDurationMinutes: lockLeaseDurationMinutes
            })
        }).then(function (response) {
            //a start rejected by the server reports its reason in the body, with an error status
            return response.json().then(function (result) {
                if (!response.ok && !result.error)
                    throw new Error('HTTP ' + response.status);
                return result;
            });
        }).then(function (result) {
            if (result.error)
                showFeedback(card, result.error);
            else if (!result.started)
                showFeedback(card, 'Migration not started: another operation is already in progress.');
            refreshStatus();
        }).catch(function () {
            showFeedback(card, 'Migration start request failed.');
            refreshStatus();
        });
    }

    function showFeedback(card, message) {
        var feedback = card.querySelector('[data-role="feedback"]');
        feedback.textContent = message;
        feedback.hidden = false;

        //a new message restarts the hide timeout, instead of inheriting the one of the previous
        window.clearTimeout(Number(feedback.dataset.hideTimer));
        feedback.dataset.hideTimer = window.setTimeout(function () {
            feedback.hidden = true;
        }, FEEDBACK_TIMEOUT_MS);
    }

    function loadCollectionSizes(card) {
        var section = card.querySelector('[data-role="schemas"]');
        section.dataset.loaded = 'true';

        fetch(baseUrl + '?handler=CollectionSizes&identifier=' + encodeURIComponent(card.dataset.identifier), {
            headers: { 'Accept': 'application/json' }
        }).then(function (response) {
            if (!response.ok)
                throw new Error('HTTP ' + response.status);
            return response.json();
        }).then(function (collections) {
            collections.forEach(function (collection) {
                var element = findCollection(card, collection.repository);
                if (!element)
                    return;

                element.querySelector('[data-role="collection-size"]').textContent = collection.isUnavailable
                    ? 'size unavailable: an exclusive access is running'
                    : 'about ' + collection.estimatedDocumentsCount.toLocaleString() +
                      (collection.estimatedDocumentsCount === 1 ? ' document' : ' documents');
                element.querySelector('[data-role="count-schemas"]').disabled = collection.isUnavailable;
            });
        }).catch(function () {
            section.dataset.loaded = '';
            Array.prototype.forEach.call(card.querySelectorAll('[data-role="collection-size"]'), function (size) {
                size.textContent = 'size request failed';
            });
        });
    }

    function loadSchemaCounts(card, collection) {
        var button = collection.querySelector('[data-role="count-schemas"]');
        button.disabled = true;
        button.textContent = 'Counting…';

        fetch(baseUrl + '?handler=SchemaCounts' +
            '&identifier=' + encodeURIComponent(card.dataset.identifier) +
            '&repositoryName=' + encodeURIComponent(collection.dataset.repository), {
            headers: { 'Accept': 'application/json' }
        }).then(function (response) {
            if (!response.ok)
                throw new Error('HTTP ' + response.status);
            return response.json();
        }).then(function (counts) {
            renderSchemaCounts(collection, counts);
            button.textContent = counts.isUnavailable ? 'Count documents' : 'Recount';
        }).catch(function () {
            button.textContent = 'Count failed, retry';
        }).then(function () {
            button.disabled = false;
        });
    }

    function findCollection(card, repository) {
        var found = null;
        Array.prototype.forEach.call(card.querySelectorAll('.schema-collection'), function (candidate) {
            if (candidate.dataset.repository === repository)
                found = candidate;
        });
        return found;
    }

    function renderSchemaCounts(element, collection) {
        var body = element.querySelector('tbody');

        // Drop the rows added by a previous count, and reset the registered schema counts.
        Array.prototype.forEach.call(body.querySelectorAll('[data-role="extra-row"]'), function (row) {
            body.removeChild(row);
        });

        var schemaRows = body.querySelectorAll('[data-schema-id]');
        Array.prototype.forEach.call(schemaRows, function (row) {
            setCount(row.querySelector('[data-role="count"]'), collection.isUnavailable ? null : 0, false);
        });

        if (collection.isUnavailable)
            return;

        // Fill the counts of the registered schemas, adding a row for each unrecognized one.
        collection.schemaCounts.forEach(function (schemaCount) {
            var row = null;
            Array.prototype.forEach.call(schemaRows, function (candidate) {
                if (candidate.dataset.schemaId === schemaCount.schemaId)
                    row = candidate;
            });

            if (row)
                setCount(row.querySelector('[data-role="count"]'),
                    schemaCount.documentsCount,
                    row.dataset.active !== 'true');
            else
                body.appendChild(buildExtraRow(schemaCount.schemaId, 'unrecognized', schemaCount.documentsCount));
        });

        if (collection.documentsWithoutSchemaId > 0)
            body.appendChild(buildExtraRow(null, 'missing', collection.documentsWithoutSchemaId));

        /* Not an addend of the column: these documents are counted above under their schema
         * id, and this says how many of them carry it under the previous element name. */
        if (collection.documentsOnDeprecatedSchemaIdElement > 0)
            body.appendChild(buildExtraRow(
                null, 'deprecated-element', collection.documentsOnDeprecatedSchemaIdElement));
    }

    function buildExtraRow(schemaId, kind, documentsCount) {
        var row = document.createElement('tr');
        row.dataset.role = 'extra-row';

        var modelTypeCell = document.createElement('td');
        modelTypeCell.className = 'muted';
        modelTypeCell.textContent = '—';
        row.appendChild(modelTypeCell);

        var schemaCell = document.createElement('td');
        if (schemaId !== null) {
            var schemaIdLabel = document.createElement('span');
            schemaIdLabel.className = 'schema-id';
            schemaIdLabel.textContent = schemaId;
            schemaCell.appendChild(schemaIdLabel);
            schemaCell.appendChild(document.createTextNode(' '));
        }
        var tag = document.createElement('span');
        tag.className = 'schema-tag ' + kind;
        tag.textContent = extraRowLabel(kind);
        schemaCell.appendChild(tag);
        row.appendChild(schemaCell);

        var countCell = document.createElement('td');
        countCell.dataset.role = 'count';
        setCount(countCell, documentsCount, true);
        row.appendChild(countCell);

        return row;
    }

    function extraRowLabel(kind) {
        switch (kind) {
            case 'missing': return 'no schema id';
            case 'deprecated-element': return 'of the above, on the deprecated schema id element';
            default: return kind;
        }
    }

    function setCount(cell, documentsCount, needsMigration) {
        if (documentsCount === null) {
            cell.textContent = '—';
            cell.className = 'numeric muted';
            return;
        }

        cell.textContent = documentsCount.toLocaleString();
        cell.className = documentsCount === 0
            ? 'numeric muted'
            : 'numeric' + (needsMigration ? ' needs-migration' : '');
    }

    function scanMissingOriginReferences(card, collection) {
        var button = collection.querySelector('[data-role="scan-references"]');
        button.disabled = true;
        button.textContent = 'Scanning…';

        fetch(baseUrl + '?handler=MissingOriginReferences' +
            '&identifier=' + encodeURIComponent(card.dataset.identifier) +
            '&repositoryName=' + encodeURIComponent(collection.dataset.repository), {
            headers: { 'Accept': 'application/json' }
        }).then(function (response) {
            if (!response.ok)
                throw new Error('HTTP ' + response.status);
            return response.json();
        }).then(function (report) {
            renderMissingOriginReport(collection, report);
            button.textContent = report.isUnavailable ? 'Scan references' : 'Rescan';
        }).catch(function () {
            button.textContent = 'Scan failed, retry';
        }).then(function () {
            button.disabled = false;
        });
    }

    function renderMissingOriginReport(collection, report) {
        var container = collection.querySelector('[data-role="scan-results"]');
        container.innerHTML = '';

        var repairButton = collection.querySelector('[data-role="repair-references"]');
        var totalMissing = 0;

        if (report.isUnavailable) {
            var unavailable = document.createElement('p');
            unavailable.className = 'muted';
            unavailable.textContent = 'Scan unavailable: an exclusive access is running.';
            container.appendChild(unavailable);
        } else if (report.pathReports.length === 0) {
            var noReferences = document.createElement('p');
            noReferences.className = 'muted';
            noReferences.textContent = 'The documents of this collection carry no verifiable reference.';
            container.appendChild(noReferences);
        } else {
            var table = document.createElement('table');
            table.className = 'schemas-table';

            var head = document.createElement('thead');
            var headRow = document.createElement('tr');
            var titles = ['Reference path', 'Origin collection', 'Missing origins', 'Referencing documents', 'On origin delete'];
            //the chosen action is a control: it renders only where a repair can run
            if (repairButton)
                titles.push('Repair with');
            titles.forEach(function (title, index) {
                var cell = document.createElement('th');
                cell.textContent = title;
                if (index === 2 || index === 3)
                    cell.className = 'numeric';
                headRow.appendChild(cell);
            });
            head.appendChild(headRow);
            table.appendChild(head);

            var body = document.createElement('tbody');
            report.pathReports.forEach(function (pathReport) {
                totalMissing += pathReport.missingOriginIdsCount;
                body.appendChild(buildMissingOriginRow(pathReport, repairButton !== null));
            });
            table.appendChild(body);
            container.appendChild(table);
        }

        //the paths the scan can't verify, whose references stay untouched
        if (!report.isUnavailable && report.unverifiableElementPaths.length > 0) {
            var unverifiable = document.createElement('p');
            unverifiable.className = 'muted';
            var tag = document.createElement('span');
            tag.className = 'shape-tag unverifiable';
            tag.textContent = 'unverifiable';
            unverifiable.appendChild(tag);
            unverifiable.appendChild(document.createTextNode(
                ' ' + report.unverifiableElementPaths.join(', ')));
            container.appendChild(unverifiable);
        }

        if (repairButton) {
            repairButton.hidden = totalMissing === 0;
            collection.querySelector('[data-role="repair-references-dry-run"]').hidden = totalMissing === 0;
        }
    }

    //what the mapping declares, and what a repair does with it
    function originDeleteLabel(originDelete) {
        switch (originDelete) {
            case 'KeepReference': return 'Keep the reference';
            case 'DeleteReferencingDocument': return 'Delete the document';
            default: return 'Remove the reference';
        }
    }

    /* The action applied to a path, defaulted to the policy its mapping declares: the
     * operator can change it before confirming, and only the chosen actions travel. */
    function buildRepairModeSelect(pathReport) {
        var select = document.createElement('select');
        select.dataset.role = 'repair-mode';
        select.dataset.elementPath = pathReport.elementPath;

        ['KeepReference', 'RemoveReference', 'DeleteReferencingDocument'].forEach(function (mode) {
            var option = document.createElement('option');
            option.value = mode;
            option.textContent = originDeleteLabel(mode);
            option.selected = mode === pathReport.originDelete;
            select.appendChild(option);
        });

        return select;
    }

    function buildMissingOriginRow(pathReport, withRepairMode) {
        var row = document.createElement('tr');

        var pathCell = document.createElement('td');
        var pathLabel = document.createElement('span');
        pathLabel.className = 'schema-id';
        pathLabel.textContent = pathReport.elementPath;
        pathCell.appendChild(pathLabel);
        row.appendChild(pathCell);

        var originCell = document.createElement('td');
        originCell.textContent = pathReport.originRepositoryNames.join(', ');
        row.appendChild(originCell);

        var missingCell = document.createElement('td');
        if (pathReport.missingOriginIdsCount === 0) {
            missingCell.className = 'numeric muted';
            missingCell.textContent = '0';
        } else {
            missingCell.className = 'numeric missing-origins';

            //the missing origin ids listing, capped by the server: the count is complete
            var idsEntry = document.createElement('details');
            var idsSummary = document.createElement('summary');
            idsSummary.textContent = pathReport.missingOriginIdsCount.toLocaleString();
            idsEntry.appendChild(idsSummary);
            var idsList = document.createElement('ul');
            idsList.className = 'missing-origin-ids';
            pathReport.trackedMissingOriginIds.forEach(function (missingOriginId) {
                var idItem = document.createElement('li');
                /* The ids are document content: they must keep landing on textContent,
                 * never on innerHTML. */
                idItem.textContent = missingOriginId;
                idsList.appendChild(idItem);
            });
            if (pathReport.trackedMissingOriginIds.length < pathReport.missingOriginIdsCount) {
                var truncationItem = document.createElement('li');
                truncationItem.className = 'muted';
                truncationItem.textContent = '… and ' +
                    (pathReport.missingOriginIdsCount - pathReport.trackedMissingOriginIds.length).toLocaleString() +
                    ' more';
                idsList.appendChild(truncationItem);
            }
            idsEntry.appendChild(idsList);
            missingCell.appendChild(idsEntry);
        }
        row.appendChild(missingCell);

        var referencingCell = document.createElement('td');
        if (pathReport.missingOriginIdsCount === 0) {
            referencingCell.className = 'numeric muted';
            referencingCell.textContent = '0';
        } else {
            referencingCell.className = 'numeric missing-origins';

            /* The ids of the documents an operator would go and look at, listed under their
             * own cap; the count is over the listed missing origin ids only, so a truncated
             * listing makes it a lower bound. */
            var referencingEntry = document.createElement('details');
            var referencingSummary = document.createElement('summary');
            referencingSummary.textContent =
                (pathReport.trackedMissingOriginIds.length < pathReport.missingOriginIdsCount ? '≥ ' : '') +
                pathReport.referencingDocumentsCount.toLocaleString();
            referencingEntry.appendChild(referencingSummary);
            var referencingList = document.createElement('ul');
            referencingList.className = 'missing-origin-ids';
            pathReport.trackedReferencingDocumentIds.forEach(function (documentId) {
                var idItem = document.createElement('li');
                //document content: it must keep landing on textContent, never on innerHTML
                idItem.textContent = documentId;
                referencingList.appendChild(idItem);
            });
            if (pathReport.trackedReferencingDocumentIds.length < pathReport.referencingDocumentsCount) {
                var truncatedItem = document.createElement('li');
                truncatedItem.className = 'muted';
                truncatedItem.textContent = '… and ' +
                    (pathReport.referencingDocumentsCount - pathReport.trackedReferencingDocumentIds.length).toLocaleString() +
                    ' more';
                referencingList.appendChild(truncatedItem);
            }
            referencingEntry.appendChild(referencingList);
            referencingCell.appendChild(referencingEntry);
        }
        row.appendChild(referencingCell);

        //what the mapping declares a deleted origin does to the documents referencing it
        var originDeleteCell = document.createElement('td');
        originDeleteCell.textContent = originDeleteLabel(pathReport.originDelete);
        row.appendChild(originDeleteCell);

        if (withRepairMode) {
            var repairModeCell = document.createElement('td');
            repairModeCell.appendChild(buildRepairModeSelect(pathReport));
            row.appendChild(repairModeCell);
        }

        return row;
    }

    function startReferencesRepair(card, collection, dryRun) {
        var repository = collection.dataset.repository;
        var selects = Array.prototype.slice.call(
            collection.querySelectorAll('select[data-role="repair-mode"]'));

        var removedPaths = selects.filter(function (select) { return select.value === 'RemoveReference'; });
        var deletedPaths = selects.filter(function (select) { return select.value === 'DeleteReferencingDocument'; });
        if (removedPaths.length === 0 && deletedPaths.length === 0) {
            showRepairFeedback(collection, 'Every reference path is set to keep its references: nothing to repair.');
            return;
        }

        var message = (dryRun ? 'Dry run the repair of' : 'Repair') +
            ' the references to missing origin documents of "' + repository + '"?\n\n' +
            'It runs as an operation under the db context lock: the collection is scanned again, ' +
            'and every verified reference pointing to a missing origin document is repaired as chosen.';
        if (removedPaths.length !== 0)
            message += '\n\nReferences removed at: ' +
                removedPaths.map(function (select) { return select.dataset.elementPath; }).join(', ') +
                '. Array items are pulled out of their arrays, single references are set to null.';
        if (deletedPaths.length !== 0)
            message += '\n\nDOCUMENTS DELETED for the references at: ' +
                deletedPaths.map(function (select) { return select.dataset.elementPath; }).join(', ') +
                '. The referencing documents are deleted, and their own reference policies ' +
                'propagate in turn.';
        message += dryRun
            ? '\n\nThe dry run persists nothing: it reports what the repair would do. Data stays ' +
              'accessible while it runs.'
            : '\n\nWhile it runs, the db context denies concurrent access to data.';
        if (!window.confirm(message))
            return;

        //the lease duration of the card bounds this operation too: both claim the same lock
        var lockLeaseDurationMinutes = card.querySelector('[data-role="lock-lease-duration"]').value;

        var body = new URLSearchParams({
            identifier: card.dataset.identifier,
            repositoryName: repository,
            dryRun: dryRun,
            lockLeaseDurationMinutes: lockLeaseDurationMinutes
        });
        selects.forEach(function (select) {
            body.append('elementPaths', select.dataset.elementPath);
            body.append('repairModes', select.value);
        });

        setRepairControlsDisabled(collection, true);

        fetch(baseUrl + '?handler=RepairMissingOriginReferences', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded',
                'RequestVerificationToken': antiforgeryToken
            },
            body: body
        }).then(function (response) {
            //a start rejected by the server reports its reason in the body, with an error status
            return response.json().then(function (result) {
                if (!response.ok && !result.error)
                    throw new Error('HTTP ' + response.status);
                return result;
            });
        }).then(function (result) {
            if (result.error)
                showRepairFeedback(collection, result.error);
            else if (!result.started)
                showRepairFeedback(collection, 'Repair not started: another operation is already in progress.');
            refreshStatus();
        }).catch(function () {
            showRepairFeedback(collection, 'Repair start request failed.');
            refreshStatus();
        });
    }

    function showRepairFeedback(collection, message) {
        var feedback = collection.querySelector('[data-role="repair-feedback"]');
        feedback.textContent = message;
        feedback.hidden = false;

        window.clearTimeout(Number(feedback.dataset.hideTimer));
        feedback.dataset.hideTimer = window.setTimeout(function () {
            feedback.hidden = true;
        }, FEEDBACK_TIMEOUT_MS);
    }

    function setRepairControlsDisabled(root, disabled) {
        Array.prototype.forEach.call(
            root.querySelectorAll('[data-role="repair-references"]'),
            function (repairButton) { repairButton.disabled = disabled; });
        Array.prototype.forEach.call(
            root.querySelectorAll('[data-role="repair-references-dry-run"]'),
            function (dryRunButton) { dryRunButton.disabled = disabled; });
        Array.prototype.forEach.call(
            root.querySelectorAll('select[data-role="repair-mode"]'),
            function (select) { select.disabled = disabled; });
    }

    /* The repair operations of a card, rendered on the collection they repair: the running one
     * with its paths advancing, and otherwise the last one that ran there. */
    function buildRepairPathsTable(operation) {
        var table = document.createElement('table');
        table.className = 'schemas-table';

        var head = document.createElement('thead');
        var headRow = document.createElement('tr');
        ['Reference path', 'Action', 'State', 'Missing origins', 'Updated', 'Deleted'].forEach(function (title, index) {
            var cell = document.createElement('th');
            cell.textContent = title;
            if (index >= 3)
                cell.className = 'numeric';
            headRow.appendChild(cell);
        });
        head.appendChild(headRow);
        table.appendChild(head);

        var body = document.createElement('tbody');
        operation.pathStates.forEach(function (pathState) {
            var row = document.createElement('tr');

            var pathCell = document.createElement('td');
            var pathLabel = document.createElement('span');
            pathLabel.className = 'schema-id';
            pathLabel.textContent = pathState.elementPath;
            pathCell.appendChild(pathLabel);
            row.appendChild(pathCell);

            var modeCell = document.createElement('td');
            modeCell.textContent = originDeleteLabel(pathState.repairMode);
            row.appendChild(modeCell);

            var stateCell = document.createElement('td');
            var stateBadge = document.createElement('span');
            stateBadge.className = 'status-badge ' + repairStateBadgeClass(pathState.state);
            stateBadge.textContent = pathState.state;
            stateCell.appendChild(stateBadge);
            if (pathState.errorMessage) {
                var error = document.createElement('div');
                error.className = 'repair-path-error';
                //the message quotes the exception that failed the path: never innerHTML
                error.textContent = pathState.errorMessage;
                stateCell.appendChild(error);
            }
            row.appendChild(stateCell);

            [pathState.missingOriginIdsCount, pathState.updatedDocumentsCount, pathState.deletedDocumentsCount]
                .forEach(function (count) {
                    var cell = document.createElement('td');
                    cell.className = 'numeric' + (count === 0 ? ' muted' : '');
                    cell.textContent = count.toLocaleString();
                    row.appendChild(cell);
                });

            body.appendChild(row);
        });
        table.appendChild(body);

        return table;
    }

    function repairStateBadgeClass(state) {
        switch (state) {
            case 'Succeded': return 'idle';
            case 'Failed': return 'locked';
            case 'Skipped': return 'cancelled';
            case 'Executing': return 'running';
            default: return '';
        }
    }

    function refreshStatus() {
        fetch(baseUrl + '?handler=Status', {
            headers: { 'Accept': 'application/json' }
        }).then(function (response) {
            if (!response.ok)
                throw new Error('HTTP ' + response.status);
            return response.json();
        }).then(function (statuses) {
            banner.hidden = true;

            var anyLocked = false;
            statuses.forEach(function (status) {
                if (status.isLocked)
                    anyLocked = true;

                cards.forEach(function (card) {
                    if (card.dataset.identifier === status.identifier)
                        renderCard(card, status);
                });
            });

            schedule(anyLocked ? POLL_ACTIVE_MS : POLL_IDLE_MS);
        }).catch(function () {
            banner.hidden = false;
            schedule(POLL_IDLE_MS);
        });
    }

    function schedule(delayMs) {
        window.clearTimeout(pollTimer);
        pollTimer = window.setTimeout(refreshStatus, delayMs);
    }

    function renderCard(card, status) {
        // Skip DOM rebuild when nothing changed, it would close open <details> and reset scroll.
        var payload = JSON.stringify(status);
        if (card.dataset.lastPayload === payload)
            return;
        card.dataset.lastPayload = payload;

        var badge = card.querySelector('[data-role="status"]');
        if (status.runningOperation) {
            badge.textContent = status.runningOperation.isDryRun ? 'Dry run' : 'Migrating';
            badge.className = 'status-badge running';
        } else if (status.isLocked) {
            badge.textContent = 'Locked';
            badge.className = 'status-badge locked';
        } else {
            badge.textContent = 'Idle';
            badge.className = 'status-badge idle';
        }

        card.querySelector('[data-role="start"]').disabled = status.isLocked;
        card.querySelector('[data-role="start-dry-run"]').disabled = status.isLocked;
        card.querySelector('[data-role="stop-at-first-error"]').disabled = status.isLocked;
        card.querySelector('[data-role="rewrite-deprecated-schemas"]').disabled = status.isLocked;
        card.querySelector('[data-role="lock-lease-duration"]').disabled = status.isLocked;

        renderRunningOperation(card.querySelector('[data-role="live"]'), status.runningOperation);

        /* The expanded history is loaded on demand, page by page: rebuilding it from the
         * polled status would drop every older page the operator walked to. */
        if (card.dataset.historyExpanded !== 'true')
            renderHistory(card.querySelector('[data-role="history"]'), status.lastOperations);

        setRepairControlsDisabled(card, status.isLocked);
    }

    /* Whatever runs on the db context reports in one place, at the top of the card: the kinds
     * share the db context lock, so at most one of them is running. */
    function renderRunningOperation(container, operation) {
        container.innerHTML = '';
        container.hidden = !operation;
        if (!operation)
            return;

        var heading = document.createElement('h4');
        heading.appendChild(document.createElement('span')).className = 'spinner';
        heading.appendChild(document.createTextNode(
            (operation.isDryRun ? 'Dry run — ' : '') + operationKindLabel(operation.kind) + ' in progress'));
        container.appendChild(heading);

        var meta = document.createElement('p');
        meta.className = 'migration-live-meta';
        meta.textContent = 'operation ' + operation.id +
            (operation.repository ? ' on "' + operation.repository + '"' : '') +
            ' — ' + operation.status +
            ' since ' + formatDateTime(operation.creationDateTime) +
            (operation.rewriteDeprecatedSchemas ? ' — rewrites the deprecated schemas' : '') +
            (operation.stopAtFirstError ? ' — stops at the first failing document' : '');
        container.appendChild(meta);

        //each kind reports what it records: the migration logs, the state of each repaired path
        if (operation.kind === 'ReferencesRepair') {
            container.appendChild(buildRepairPathsTable(operation));
            return;
        }

        var logList = document.createElement('ol');
        logList.className = 'log-list';
        renderLogs(logList, operation.logs, true);
        container.appendChild(logList);
    }

    function toggleHistoryExpansion(card) {
        if (card.dataset.historyExpanded === 'true') {
            collapseHistory(card);
            return;
        }

        card.dataset.historyExpanded = 'true';
        card.querySelector('[data-role="history-title"]').textContent = 'All operations';
        card.querySelector('[data-role="expand-history"]').textContent = 'Show latest';
        card.querySelector('[data-role="history"]').innerHTML = '';
        loadHistoryPage(card, 0);
    }

    function collapseHistory(card) {
        card.dataset.historyExpanded = 'false';
        card.querySelector('[data-role="history-title"]').textContent = 'Latest operations';
        card.querySelector('[data-role="expand-history"]').textContent = 'Show all';

        /* Back to the polled view: the payload of the last refresh already carries the latest
         * operations, so the block renders without waiting for the next poll. */
        var lastPayload = card.dataset.lastPayload;
        renderHistory(
            card.querySelector('[data-role="history"]'),
            lastPayload ? JSON.parse(lastPayload).lastOperations : []);
    }

    function loadHistoryPage(card, page) {
        var container = card.querySelector('[data-role="history"]');
        var loading = document.createElement('p');
        loading.className = 'muted';
        loading.textContent = 'Loading…';
        container.appendChild(loading);

        fetch(baseUrl + '?handler=Operations' +
            '&identifier=' + encodeURIComponent(card.dataset.identifier) +
            '&historyPage=' + page, {
            headers: { 'Accept': 'application/json' }
        }).then(function (response) {
            if (!response.ok)
                throw new Error('HTTP ' + response.status);
            return response.json();
        }).then(function (result) {
            container.removeChild(loading);
            appendHistoryPage(card, container, result);
        }).catch(function () {
            loading.textContent = 'History page request failed.';
        });
    }

    function appendHistoryPage(card, container, result) {
        if (result.operations.length === 0) {
            //an empty first page is an empty history, an empty older one closes the walk
            if (container.childElementCount === 0) {
                var empty = document.createElement('p');
                empty.className = 'muted';
                empty.textContent = 'No operations executed yet.';
                container.appendChild(empty);
            }
            return;
        }

        result.operations.forEach(function (operation) {
            container.appendChild(buildHistoryEntry(operation));
        });

        //only a full page can be followed by another one
        if (!result.hasMore)
            return;

        var older = document.createElement('button');
        older.type = 'button';
        older.className = 'btn secondary history-older';
        older.textContent = 'Load older';
        older.addEventListener('click', function () {
            container.removeChild(older);
            loadHistoryPage(card, result.historyPage + 1);
        });
        container.appendChild(older);
    }

    function renderLogs(list, logs, scrollToBottom) {
        list.innerHTML = '';
        logs.forEach(function (log) {
            var entry = document.createElement('li');
            entry.className = 'log-entry ' + log.state.toLowerCase();

            var time = document.createElement('time');
            time.textContent = formatTime(log.creationDateTime);
            entry.appendChild(time);

            var state = document.createElement('span');
            state.className = 'log-state';
            state.textContent = log.state.toUpperCase();
            entry.appendChild(state);

            var description = document.createElement('span');
            description.textContent = log.description;
            entry.appendChild(description);

            list.appendChild(entry);

            //failing documents reported by the migration
            if (log.errors && log.errors.length) {
                var errorsEntry = document.createElement('li');
                errorsEntry.className = 'log-errors';
                var errorsList = document.createElement('ul');
                log.errors.forEach(function (error) {
                    var errorItem = document.createElement('li');
                    /* The error message quotes the exception that failed the document, so it
                     * can carry document content: it must keep landing on textContent, never
                     * on innerHTML. */
                    errorItem.textContent = error.documentId + ' — ' + error.message;
                    errorsList.appendChild(errorItem);
                });
                errorsEntry.appendChild(errorsList);
                list.appendChild(errorsEntry);
            }
        });

        if (scrollToBottom)
            list.scrollTop = list.scrollHeight;
    }

    function renderHistory(container, operations) {
        container.innerHTML = '';

        if (operations.length === 0) {
            var empty = document.createElement('p');
            empty.className = 'muted';
            empty.textContent = 'No operations executed yet.';
            container.appendChild(empty);
            return;
        }

        operations.forEach(function (operation) {
            container.appendChild(buildHistoryEntry(operation));
        });
    }

    //what an operation is, whatever it records: the kinds sharing the operations collection
    function operationKindLabel(kind) {
        switch (kind) {
            case 'ReferencesRepair': return 'References repair';
            case 'Seed': return 'Seed';
            default: return 'Migration';
        }
    }

    function buildHistoryEntry(operation) {
        /* A seeding records nothing but its existence — it is written only when a seed
         * succeeds — so it renders as a line, without a detail to expand. */
        if (operation.kind === 'Seed') {
            var seedEntry = document.createElement('div');
            seedEntry.className = 'history-entry history-entry-flat';

            var seedBadge = document.createElement('span');
            seedBadge.className = 'status-badge idle';
            seedBadge.textContent = 'Seed';
            seedEntry.appendChild(seedBadge);

            var seedDates = document.createElement('span');
            seedDates.className = 'history-dates';
            seedDates.textContent = 'seeded ' + formatDateTime(operation.creationDateTime);
            seedEntry.appendChild(seedDates);

            return seedEntry;
        }

        var entry = document.createElement('details');
        entry.className = 'history-entry';

        var summary = document.createElement('summary');

        var kindBadge = document.createElement('span');
        kindBadge.className = 'status-badge kind';
        kindBadge.textContent = operationKindLabel(operation.kind);
        summary.appendChild(kindBadge);

        var badge = document.createElement('span');
        badge.className = 'status-badge ' + historyBadgeClass(operation.status);
        badge.textContent = operation.status;
        summary.appendChild(badge);

        if (operation.isDryRun) {
            var dryRunBadge = document.createElement('span');
            dryRunBadge.className = 'status-badge dry-run';
            dryRunBadge.textContent = 'Dry run';
            summary.appendChild(dryRunBadge);
        }

        if (operation.rewriteDeprecatedSchemas) {
            var rewriteBadge = document.createElement('span');
            rewriteBadge.className = 'status-badge';
            rewriteBadge.textContent = 'Rewrite deprecated schemas';
            summary.appendChild(rewriteBadge);
        }

        if (operation.stopAtFirstError) {
            var stopBadge = document.createElement('span');
            stopBadge.className = 'status-badge';
            stopBadge.textContent = 'Stop at first error';
            summary.appendChild(stopBadge);
        }

        if (operation.repository) {
            var repositoryLabel = document.createElement('span');
            repositoryLabel.className = 'schema-id';
            repositoryLabel.textContent = operation.repository;
            summary.appendChild(repositoryLabel);
        }

        var dates = document.createElement('span');
        dates.className = 'history-dates';
        //a cancelled operation never executed: it has no completion instant to render
        dates.textContent = 'started ' + formatDateTime(operation.creationDateTime) +
            (operation.completedDateTime
                ? ' — completed ' + formatDateTime(operation.completedDateTime)
                : (operation.status === 'Cancelled' ? ' — cancelled before executing' : ''));
        summary.appendChild(dates);

        entry.appendChild(summary);

        //each kind expands into what it records: the migration logs, the repaired paths
        if (operation.kind === 'ReferencesRepair') {
            entry.appendChild(buildRepairPathsTable(operation));
        } else {
            var logList = document.createElement('ol');
            logList.className = 'log-list';
            renderLogs(logList, operation.logs, false);
            entry.appendChild(logList);
        }

        return entry;
    }

    function historyBadgeClass(status) {
        switch (status) {
            case 'Completed': return 'idle';
            case 'Failed': return 'locked';
            case 'Cancelled': return 'cancelled';
            case 'Running':
            case 'New': return 'running';
            default: return '';
        }
    }

    function formatDateTime(value) {
        return value ? new Date(value).toLocaleString() : 'unknown time';
    }

    function formatTime(value) {
        return value ? new Date(value).toLocaleTimeString() : '';
    }
})();
