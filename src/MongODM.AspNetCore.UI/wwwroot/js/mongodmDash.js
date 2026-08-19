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
         * read, and the removal control renders only on the writable repositories. */
        Array.prototype.forEach.call(card.querySelectorAll('.missing-origin-collection'), function (collection) {
            collection.querySelector('[data-role="scan-references"]').addEventListener('click', function () {
                scanMissingOriginReferences(card, collection);
            });

            var removeButton = collection.querySelector('[data-role="remove-references"]');
            if (removeButton)
                removeButton.addEventListener('click', function () {
                    removeMissingOriginReferences(card, collection);
                });
        });
    });

    cards.forEach(function (card) {
        card.querySelector('[data-role="start"]').addEventListener('click', function () {
            startMigration(card, false);
        });
        card.querySelector('[data-role="start-dry-run"]').addEventListener('click', function () {
            startMigration(card, true);
        });
    });

    if (cards.length !== 0)
        refreshStatus();

    function startMigration(card, dryRun) {
        var identifier = card.dataset.identifier;
        var stopAtFirstError = card.querySelector('[data-role="stop-at-first-error"]').checked;
        //the lease duration is validated server side too: the control only bounds the ordinary case
        var lockLeaseDurationMinutes = card.querySelector('[data-role="lock-lease-duration"]').value;
        var message = dryRun
            ? 'Start migration dry run on "' + identifier + '"?\n\n' +
              'The dry run simulates the migration without persisting anything, reporting the ' +
              'failing documents. Data stays accessible while it runs.'
            : 'Start migration on "' + identifier + '"?\n\n' +
              'While the migration is running, the db context denies concurrent access to data.';
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
        tag.textContent = kind === 'missing' ? 'no schema id' : kind;
        schemaCell.appendChild(tag);
        row.appendChild(schemaCell);

        var countCell = document.createElement('td');
        countCell.dataset.role = 'count';
        setCount(countCell, documentsCount, true);
        row.appendChild(countCell);

        return row;
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

        var removeButton = collection.querySelector('[data-role="remove-references"]');
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
            ['Reference path', 'Origin collection', 'Missing origins', 'Referencing documents'].forEach(function (title, index) {
                var cell = document.createElement('th');
                cell.textContent = title;
                if (index >= 2)
                    cell.className = 'numeric';
                headRow.appendChild(cell);
            });
            head.appendChild(headRow);
            table.appendChild(head);

            var body = document.createElement('tbody');
            report.pathReports.forEach(function (pathReport) {
                totalMissing += pathReport.missingOriginIdsCount;
                body.appendChild(buildMissingOriginRow(pathReport));
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

        if (removeButton)
            removeButton.hidden = totalMissing === 0;
    }

    function buildMissingOriginRow(pathReport) {
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
            //counted over the listed ids only: a truncated listing makes it a lower bound
            referencingCell.textContent =
                (pathReport.trackedMissingOriginIds.length < pathReport.missingOriginIdsCount ? '≥ ' : '') +
                pathReport.referencingDocumentsCount.toLocaleString();
        }
        row.appendChild(referencingCell);

        return row;
    }

    function removeMissingOriginReferences(card, collection) {
        var repository = collection.dataset.repository;
        if (!window.confirm('Remove the references to missing origin documents from "' + repository + '"?\n\n' +
            'The collection is scanned again, and every verified reference pointing to a missing origin ' +
            'document is removed: array items are pulled out of their arrays, single references are set ' +
            'to null. No document is deleted.'))
            return;

        var removeButton = collection.querySelector('[data-role="remove-references"]');
        var outcome = collection.querySelector('[data-role="removal-outcome"]');
        removeButton.disabled = true;
        outcome.hidden = true;

        fetch(baseUrl + '?handler=RemoveMissingOriginReferences', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded',
                'RequestVerificationToken': antiforgeryToken
            },
            body: new URLSearchParams({
                identifier: card.dataset.identifier,
                repositoryName: repository
            })
        }).then(function (response) {
            //a removal rejected by the server reports its reason in the body, with an error status
            return response.json().then(function (result) {
                if (!response.ok && !result.error)
                    throw new Error('HTTP ' + response.status);
                return result;
            });
        }).then(function (result) {
            if (result.error) {
                outcome.textContent = result.error;
            } else {
                var removedIds = 0;
                var updatedDocuments = 0;
                result.pathRemovals.forEach(function (pathRemoval) {
                    removedIds += pathRemoval.missingOriginIdsCount;
                    updatedDocuments += pathRemoval.updatedDocumentsCount;
                });
                outcome.textContent = 'Removed the references to ' + removedIds.toLocaleString() +
                    ' missing origin documents, updating ' + updatedDocuments.toLocaleString() + ' documents.';
            }
            outcome.hidden = false;

            //rescan to render the repaired state
            scanMissingOriginReferences(card, collection);
        }).catch(function () {
            outcome.textContent = 'Removal request failed.';
            outcome.hidden = false;
        }).then(function () {
            removeButton.disabled = false;
        });
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
        card.querySelector('[data-role="lock-lease-duration"]').disabled = status.isLocked;

        var live = card.querySelector('[data-role="live"]');
        var logList = card.querySelector('[data-role="logs"]');
        if (status.runningOperation) {
            live.hidden = false;
            card.querySelector('[data-role="live-meta"]').textContent =
                (status.runningOperation.isDryRun ? 'Dry run operation ' : 'Operation ') +
                status.runningOperation.id +
                ' — ' + status.runningOperation.status +
                ' since ' + formatDateTime(status.runningOperation.creationDateTime) +
                (status.runningOperation.stopAtFirstError ? ' — stops at the first failing document' : '');
            renderLogs(logList, status.runningOperation.logs, true);
        } else {
            live.hidden = true;
            logList.innerHTML = '';
        }

        renderHistory(card.querySelector('[data-role="history"]'), status.lastOperations);
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
            empty.textContent = 'No migrations executed yet.';
            container.appendChild(empty);
            return;
        }

        operations.forEach(function (operation) {
            var entry = document.createElement('details');
            entry.className = 'history-entry';

            var summary = document.createElement('summary');

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

            if (operation.stopAtFirstError) {
                var stopBadge = document.createElement('span');
                stopBadge.className = 'status-badge';
                stopBadge.textContent = 'Stop at first error';
                summary.appendChild(stopBadge);
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

            var logList = document.createElement('ol');
            logList.className = 'log-list';
            renderLogs(logList, operation.logs, false);
            entry.appendChild(logList);

            container.appendChild(entry);
        });
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
