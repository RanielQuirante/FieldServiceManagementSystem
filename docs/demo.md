# Demo

This walkthrough shows FieldService end to end with the seeded demo data: an administrator assigns and follows jobs on the dashboard while a technician works through them in the app, and every change appears on the dashboard without a reload. It takes about 15 minutes.

## Before you start

1. Set up the repository as described in [development.md](development.md), including the one-time `create-dev-env.mjs` run.
2. Start the containers with the dashboard: `docker compose -f infrastructure/docker-compose.yml --profile full up -d --wait`. The dashboard is at http://127.0.0.1:8081, and the API at http://127.0.0.1:5080.
3. Start the technician app in a Debug build ([development.md](development.md#6-run-the-technician-app)): on the Android emulator it calls the API at `10.0.2.2:5080`, and on Windows at `localhost:5080`.
4. Show the demo password once, at your own terminal: `node infrastructure/scripts/create-dev-env.mjs --show-demo-password`. Every demo account uses it.

On an empty database the API seeds the demo data at its first start: one administrator, six technicians and 20 jobs, JOB-1036 to JOB-1055, with times relative to the day of the seed. The accounts used below:

| Person | Email | Role |
|---|---|---|
| Dana Whitfield | `dana.whitfield@fieldservice.example` | Administrator (shown as Dispatcher) |
| Marco Reyes | `marco.reyes@fieldservice.example` | Technician, HVAC |
| Aisha Karim | `aisha.karim@fieldservice.example` | Technician, Electrical |

All names, addresses and phone numbers in the demo data are fictional.

## Walkthrough

### 1. The dashboard

Sign in to the dashboard as Dana. The dashboard shows today's status counts, the jobs that need a technician, live activity and each technician's workload. The top bar reads **Live** while the dashboard is connected for real-time updates.

### 2. Assign a job

Open **Jobs** and select **JOB-1051**, "Replace faulty breaker panel", which has no technician. In the assign panel choose **Aisha Karim** and assign. The job's status changes to Assigned, and the history records who assigned it and when.

### 3. Create a job

Open **New job**, fill in a title, customer, site address, priority, checklist template and schedule, and create it. Leaving the site address empty shows the field's error. The new job takes the next number, JOB-1056, and appears in the list with no technician.

### 4. The technician's day

Keep the dashboard open on **Jobs**, and sign in to the technician app as Marco. **My jobs** groups his active jobs by day.

Open **JOB-1047**, "Walk-in freezer temperature alarm", which is Assigned. **Navigate** opens the maps app at 88 Elm Street; on Android, **Call** opens the dialer with the site contact's number. Tap **Start job**: within a second the dashboard's row shows In progress, with no reload.

### 5. Hold and resume

Tap **Update status**, choose **Put on hold**, give a reason such as "Waiting for a replacement part", and confirm. The job shows On hold with its reason, here and on the dashboard. Open **Update status** again and choose **Resume**.

### 6. Inspect and submit

Open **JOB-1042**, "Rooftop HVAC unit not cooling", which is In progress, and choose **Continue inspection**. Answer the checklist items: enter readings, mark one item **Fail** with a fault note, and mark another **N/A**. Go back and reopen the inspection: the answers are kept.

When every item is answered, choose **Review and submit**. Check the summary, confirm the site safety check and submit. The job becomes Completed and read-only in the app. On the dashboard, open the job and then its **Inspection report**: the results, the failed item with its note and the readings are there. The seeded **JOB-1039** shows a report submitted earlier.

### 7. Cancel a job

On the dashboard, open JOB-1056, the job you created, choose **Cancel job**, give a reason and confirm. The job moves to Cancelled, and the reason appears in its history.

### 8. Real-time recovery

Stop the notification service: `docker compose -f infrastructure/docker-compose.yml stop notifications`. After a few seconds the dashboard shows **Reconnecting**, then **Live updates paused**. Change a job in the technician app; the change still saves. Start the service again with `docker compose -f infrastructure/docker-compose.yml start notifications`. The dashboard shows **Back online** and refetches, so the change you made is there.

### 9. Offline in the technician app

Turn on airplane mode on the emulator, or disconnect the Windows device from the network. The app shows the offline banner with the time the jobs were loaded, and the loaded jobs stay readable. Turn the network back on and choose **Try again**.

## Reset the demo data

The demo changes the data, so reset it before the next run.

- **Containers:** stop the stack and delete its database volume with `docker compose -f infrastructure/docker-compose.yml --profile full down -v`, then start it again with `docker compose -f infrastructure/docker-compose.yml --profile full up -d --wait`. The API seeds the demo data again on the empty database. `-v` deletes only this project's database volume.
- **Local database:** run the VS Code task **db: reset**, or its terminal equivalent in [development.md](development.md#resets). The next API start seeds the demo data again.

The demo times are relative to the seed day, so a reset also brings the schedule back to today.
