# Cosmorph retention ideas

Status: Exploratory product options

Audience: Product, design, engineering, AI safety, analytics, and development agents

Decision state: None of these ideas is approved for implementation by this document

## How an agent should use this document

This is an opportunity backlog, not a specification.

When a task concerns engagement, onboarding, returning users, notifications, chapters, spectators, followed entities, recaps, discovery, social features, or live content:

1. Read this document for relevant options and constraints.
2. Identify the user need and time horizon being addressed.
3. Prefer features that create attachment, understanding, agency, or social connection.
4. Explain expected user value, implementation cost, safety risks, and how success would be measured.
5. Prefer a small reversible experiment over a broad engagement framework.
6. Do not implement a retention idea until an issue or product decision explicitly selects it.
7. Do not optimize for time spent, notification opens, or daily activity in isolation.

## Product principle

Cosmorph should encourage return because:

> Something meaningful happened to my world, I understand why, and I care what happens next.

The world should progress without demanding attendance. Returning should feel like rejoining a living history, not collecting compensation for a chore.

## Non-negotiable guardrails

- Do not require daily attendance to prevent catastrophe.
- Do not punish missed days, break streaks, decay paid benefits, or shame users for leaving.
- Do not create fake emergencies, misleading countdowns, or false claims that an entity needs immediate help.
- Do not let the Worldmind directly decide when, how, or why to send a notification.
- Do not use notification volume, session length, or raw daily active users as the only definition of success.
- Do not hide important causal explanations to manufacture curiosity.
- Do not create repetitive maintenance tasks solely to increase interactions.
- Do not use randomized rewards, variable-ratio reward schedules, or paid recovery as the main return mechanism.
- Do not destroy a user's entire relationship with Cosmorph because a followed species became extinct.
- Do not expose private-world events through discovery, sharing, analytics, or notifications.
- Let users configure notification subject, severity, channel, quiet hours, and digest frequency.
- Make unsubscribe, pause, archive, and delete controls clear.
- Treat generated narration as presentation, not authoritative fact.

## Retention model

Cosmorph can support several durable motivations:

| Motivation | Cosmorph expression |
| --- | --- |
| Autonomy | Set Warden charters, priorities, taboos, and long-term intent |
| Competence | Learn to read ecological state and make better predictions |
| Relatedness | Care about Wardens, species, lineages, communities, and shared worlds |
| Curiosity | Follow unresolved ecological questions and emerging mysteries |
| Attachment | Name and follow living entities with persistent histories |
| Continuity | Return to a world that remembers prior actions and consequences |
| Expression | Customize the Observatory, Wardens, planets, and Chronicle presentation |
| Social meaning | Watch, predict, discuss, and commemorate events with others |
| Discovery | Find unfamiliar adaptations, behaviors, regions, and alternate outcomes |
| Legacy | Leave lineages, fossils, landmarks, myths, records, and world-changing decisions |

No single feature must satisfy every motivation. A healthy release should support several without turning them into mandatory chores.

## The core return loop

An effective returning session can be:

1. Reorientation — show the world as the user last saw it.
2. Change — visualize what happened while they were away.
3. Meaning — explain the most consequential events and their causes.
4. Focus — identify one or two unresolved questions.
5. Agency — offer one meaningful Warden decision or prediction.
6. Anticipation — show what may happen next and when another update could be interesting.
7. Exit — allow the user to leave without unfinished maintenance.

The loop should work in a few minutes but allow deeper exploration.

## Time horizons

### Session horizon

Target experience:

- Understand what changed.
- Inspect one interesting event.
- Make or revise one meaningful decision.
- Leave with a question about the future.

Avoid:

- Inbox cleanup.
- Resource collection.
- Repetitive acknowledgements.
- Many low-impact decisions.

### Several-day horizon

Target experience:

- Follow a migration, outbreak, drought, recovery, succession, or relationship.
- Observe whether a Warden strategy is working.
- Update a prediction as evidence changes.

### Chapter horizon

Target experience:

- Resolve or transform a major ecological pressure.
- Close several open questions.
- Produce a durable recap, snapshot, and legacy.
- Introduce the next pressure without invalidating the prior story.

### Season horizon

Target experience:

- Integrate a major new ecological capability or content family.
- Let existing worlds express the same release differently.
- Compare worlds and revisit old strategies.
- Preserve all historical context.

### Multi-year horizon

Target experience:

- See ages, lineages, extinctions, recoveries, migrations, and environmental transformations.
- Maintain an explorable Chronicle.
- Pass ownership or stewardship to others.
- Revisit dormant worlds.
- Export a lasting record.

## Highest-priority feature ideas

### 1. Previously on Cosmorph

When a user returns:

- Show the prior planet state.
- Animate major changes over elapsed time.
- Present three to five important events.
- Explain what the Warden did.
- Identify one unresolved question.
- Offer a deeper view without forcing it.

The recap length should adapt to absence duration and event significance, not list every tick.

Requirements:

- Deterministic event selection from authoritative Chronicle data.
- Clear separation between factual state, inferred causes, and generated narration.
- Replayable recap.
- Reduced-motion alternative.
- Accessible text summary.
- No model call required for the factual version.

### 2. Followable entities

Allow users to follow:

- Species and subspecies.
- Exceptional individual creatures.
- Herds, packs, colonies, or settlements.
- Wardens.
- Habitats, watersheds, and geographic landmarks.
- Evolutionary lineages.
- Ecological questions.

Following should power a personalized Observatory feed and optional digest. It must not grant control or guarantee survival.

Each followed entity benefits from:

- Stable identity.
- Name and visual signature.
- Current condition.
- Relationships.
- Important past events.
- Descendants or successors.
- Open threats and opportunities.

### 3. Warden relationship

A Warden should develop a recognizable decision history without escaping its fixed capabilities.

Possible features:

- Decision explanations.
- Confidence and uncertainty.
- Recurring behavioral tendencies.
- Requests for user guidance.
- Relationships with other Wardens.
- Memory of prior successes and failures.
- Charter-change comparison.
- End-of-chapter self-review.

Users should occasionally disagree with a Warden. The interesting decision is how to revise its charter, not whether to micromanage every action.

The Warden must not flatter, emotionally pressure, impersonate a dependent being, or claim consciousness.

### 4. Causal explanations

Let users ask structured questions:

- Why is this forest dying?
- What changed here?
- Why did the population migrate?
- Which conditions caused this outbreak?
- What did my Warden influence?
- Which outcome was selected by the Worldmind?

Build explanations from authoritative causal records where possible:

- Inputs.
- Rule version.
- Triggered thresholds.
- Candidate outcomes.
- Accepted decision.
- Applied state changes.
- Uncertainty.

Use generated prose only to summarize verified explanation data.

### 5. Forecasts and uncertainty

Show plausible near-term outcomes with confidence ranges:

- Drought risk.
- Migration likelihood.
- Population trend.
- Disease spread.
- Food-web instability.
- Chapter transition pressure.

Forecasts should be falsifiable and later scored. Avoid presenting uncertainty as certainty.

Users may revise predictions as new evidence appears. Prediction rewards should be recognition, knowledge, or cosmetics rather than ecological power.

### 6. Open questions and mysteries

The system can maintain explicit world questions such as:

- Will the northern forest recover before the herds return?
- Why did the ember whales disappear?
- Can two predator species coexist?
- Is a new mutation beneficial, parasitic, or neutral?
- What is producing heat beneath the ice?

Each question needs:

- Origin event.
- Known evidence.
- Unknowns.
- Possible resolution conditions.
- Related entities and locations.
- State: open, evolving, answered, disproven, or abandoned.

Mysteries should emerge from real world state and content rules. The Worldmind may frame or connect them but must not fabricate hidden state that the engine cannot later support.

### 7. Chapter arcs

A chapter should provide medium-term structure:

- Theme or pressure.
- Several open questions.
- Visible turning points.
- Optional Warden objectives.
- A closing snapshot.
- Consequence summary.
- Permanent legacies.
- A preview of the next chapter.

Do not require identical plot beats across worlds. Content defines pressures and possibilities; world state determines expression.

### 8. Chronicle and time-lapse

The Chronicle should be enjoyable to explore, not just an audit log.

Possible views:

- Illustrated timeline.
- Planet before-and-after slider.
- Species lineage tree.
- Migration map.
- Habitat history.
- Warden decision history.
- Chapter documentary.
- Extinction and recovery memorials.
- World records and unusual events.

Every presentation must remain traceable to authoritative events.

### 9. Legacy after extinction or collapse

Loss can strengthen attachment if it remains meaningful.

Possible continuations:

- Descendant or related species.
- Fossils and archaeological layers.
- Genetic traits inherited elsewhere.
- Ecological niches left behind.
- Landmarks or names.
- Myths in an intelligent culture.
- Warden reassignment.
- Restoration or memorial objectives.
- A permanent Chronicle entry.

Avoid one-click reversal that makes loss meaningless. Avoid irreversible account loss that makes return pointless.

### 10. Spectator identity

Spectators can develop continuity without becoming combatants.

Possible roles:

- Naturalist.
- Archivist.
- Forecaster.
- Cartographer.
- Documentary curator.
- Conservation advocate.

Role progression should represent knowledge, contribution, or curation rather than repetitive grinding.

Possible capabilities:

- Follow entities.
- Make predictions.
- Curate event collections.
- Name discoveries through moderated processes.
- Publish guided tours.
- Build field-guide pages.
- Participate in chapter premieres.

### 11. Social observation

Low-conflict social features:

- Shared watchlists.
- World clubs.
- Event discussion attached to a specific Chronicle item.
- Prediction groups.
- Curated tours.
- Shared annotations.
- Community chapter premieres.
- Collaborative field guides.

Require moderation, privacy controls, blocking, reporting, rate limits, and age-appropriate design before enabling user-generated communication.

Do not make unmoderated global chat an early retention feature.

### 12. Shareable story artifacts

Generate spoiler-controlled:

- Species discovery cards.
- Planet before-and-after images.
- Migration and disease maps.
- Warden decision cards.
- Extinction memorials.
- World records.
- Chapter posters.
- Short recap clips.

A shared artifact should deep-link to the relevant public world, event, time, and camera location.

Do not expose private-world facts, hidden variables, owner identity, internal prompts, or unpublished future events.

### 13. Naturalist journal and knowledge collection

Give users a personal record of what they have observed:

- Species encountered.
- Behaviors witnessed.
- Predictions made.
- Questions answered.
- Regions explored.
- Chapter participation.
- Field notes and bookmarks.

Knowledge collection should reveal understanding and history, not become a checklist of arbitrary collectibles.

### 14. World discovery

Help spectators find meaningful worlds:

- Major events happening now.
- Unusual ecosystems.
- Quiet recovery stories.
- New public worlds.
- Long-running ancient worlds.
- Worlds affected differently by the current season.
- Curated editorial selections.

Discovery ranking should not reveal private-world existence or reward harmful sensationalism. Avoid optimizing only for disasters.

### 15. Cross-world comparison

Comparison can create long-term learning:

- How the same species diverged.
- How the same seasonal pressure unfolded.
- Different Warden philosophies.
- Climate and biome outcomes.
- Recovery speed.
- Similar starting seeds with different histories.

Use normalized metrics and explain differences. Do not collapse every world into a competitive leaderboard.

### 16. World anniversaries and memory

Recognize meaningful dates:

- World creation.
- First stable ecosystem.
- First major migration.
- Chapter endings.
- A lineage surviving a world-year threshold.
- Recovery from a historic collapse.

Anniversary content should commemorate existing history, not pressure users to log in on one specific day.

### 17. User-controlled notifications

Good notification subjects:

- A followed species split into two lineages.
- A chapter ended.
- A followed migration began.
- A Warden requested guidance.
- A long-running recovery reached a threshold.
- A prediction was resolved.

Required controls:

- Off by default where appropriate.
- Immediate, daily digest, weekly digest, or none.
- Quiet hours.
- Event severity threshold.
- Per-world and per-entity follows.
- Channel control.
- Easy unsubscribe.

Notifications should be created from verified event candidates. The Worldmind may draft wording after the system decides an event is eligible, but cannot select recipients or urgency.

### 18. Return planning

Let users choose when they want to hear from a world:

- At chapter end.
- When a followed question resolves.
- If a defined risk threshold is crossed.
- In a weekly digest.
- After a chosen absence interval.

This replaces habitual checking with user-controlled anticipation.

### 19. Cinematic ambient mode

Some users may keep Cosmorph open as a living display.

Possible features:

- Automated camera tours.
- Gentle narration.
- Subtle event captions.
- Configurable world and topic focus.
- Low-power rendering mode.
- No-interaction fullscreen presentation.

Ambient time should not grant rewards or create pressure to remain connected.

### 20. Branch-and-compare experiments

In a later phase, allow authorized world owners or educators to create a temporary non-canonical branch:

- Change one Warden charter.
- Apply an alternate candidate outcome.
- Advance a limited number of ticks.
- Compare results.

Branches are educational sandboxes and never overwrite canonical history. They may be expensive and should have strict quotas.

## Pacing and novelty

An endless game needs protected pacing, not constant escalation.

Track a world-level pacing state:

- Time since last significant event.
- Number of simultaneous crises.
- Recovery time.
- Repeated event families.
- Quiet ecological development.
- Unresolved question count.
- Recent Worldmind intervention.
- Chapter progress.

Possible controls:

- Novelty budget limiting how many new pressures enter at once.
- Crisis cap preventing perpetual catastrophe.
- Recovery windows.
- Event-family cooldowns.
- Minimum evidence before escalating a mystery.
- World-specific pacing profile.

The Worldmind may choose among engine-created pacing candidates. It cannot ignore caps to make a world more dramatic.

Quiet periods should remain interesting through succession, relationships, adaptation, discovery, and visual change.

## Onboarding and early retention

The first session should quickly demonstrate the core promise.

Possible sequence:

1. Let the user choose or generate a world.
2. Introduce one visible ecological relationship.
3. Create or assign one Warden.
4. Let the user set a meaningful charter preference.
5. Advance enough world time to show a consequence.
6. Explain the consequence on the globe.
7. Introduce one followable entity and one open question.
8. End with a clear reason to return.

Do not front-load every overlay, biome, Warden capability, season system, or social feature.

For spectators:

1. Start with a curated live world.
2. Explain one visible event.
3. Let the spectator follow an entity or make a prediction.
4. Show where the story continues.

## Architecture implications

Suggested conceptual boundaries:

- Event significance: deterministic score and reason codes for recap eligibility.
- Follow graph: account-to-world, entity, question, or topic relationships.
- Return briefing: generated view over Chronicle changes since a cursor.
- Explanation record: authoritative cause, rule, candidate, decision, and effect links.
- Forecast: versioned prediction with time horizon, confidence, and later resolution.
- Open question: evidence-linked state machine.
- Chapter recap: immutable chapter summary plus presentation variants.
- Notification candidate: verified event and eligible audience category.
- Notification preference: explicit user controls and quiet hours.
- Pacing state: world-level novelty, crisis, recovery, and repetition limits.
- Personal journal: private bookmarks, observations, and prediction history.
- Share artifact: public-safe projection with explicit privacy classification.

Requirements:

- Build the factual return briefing without requiring a model.
- Generated prose cannot introduce facts absent from the briefing.
- Recompute personalized views from stable Chronicle cursors.
- Make recap and notification generation idempotent.
- Store notification eligibility separately from delivery attempts.
- Enforce world visibility before follow, sharing, discovery, and notification queries.
- Do not send directly from a simulation tick transaction.
- Never pass email, push token, account profile, or notification preferences to the Worldmind.
- Version explanation, forecast, question, recap, and pacing schemas.
- Support user data export and deletion.
- Record why a notification was eligible and which preference allowed it.

## Analytics and success criteria

Possible north-star behavior:

> A user returns to understand a meaningful consequence and then makes, follows, predicts, curates, or shares something they care about.

Measure:

- Percentage of new users who witness and understand one consequence.
- Percentage who follow an entity or question.
- Return rate after the first offline progression.
- Return rate at chapter boundaries.
- Recap completion and deeper inspection.
- Meaningful Warden decisions per returning session.
- Forecast creation and resolution viewing.
- Followed-entity revisit rate.
- Chronicle exploration.
- Spectator-to-follower and follower-to-world-owner transitions.
- Notification opt-in, unsubscribe, mute, and complaint rates.
- Return after long absence.
- User-reported understanding, attachment, autonomy, and satisfaction.
- Diversity of event types and worlds receiving attention.

Also monitor harms:

- Compulsive checking reports.
- Notification regret.
- Confusion about whether the Worldmind is conscious.
- Users believing paid or frequent attendance changes AI favor.
- Excessive crisis density.
- Worlds becoming unreadable after absence.
- Social toxicity and moderation load.
- Accessibility failures.

Do not interpret longer sessions as automatically better. A concise satisfying check-in may be a successful session.

## Experiments before broad implementation

1. Test a factual offline recap before generated narration.
2. Compare three-event and five-event return briefings.
3. Let users follow one species and measure voluntary return.
4. Test structured Warden explanations.
5. Introduce one open question per world and observe follow-through.
6. Test a chapter-ending recap with no reward attached.
7. Offer weekly digest, immediate alerts, and no alerts as explicit choices.
8. Test shareable species cards with privacy-safe public worlds.
9. Interview users after an extinction to evaluate legacy and recovery.
10. Run worlds with different pacing profiles and compare perceived interest, not only event count.

Each experiment needs:

- User hypothesis.
- Target segment.
- Primary and guardrail metric.
- Minimum observation period.
- Stop condition.
- Privacy and safety review.
- Decision recorded after results.

## Questions requiring explicit decisions

- What session frequency would feel healthy for Cosmorph?
- How long can a world progress before a recap becomes overwhelming?
- Which entities may be individualized and named?
- Can significant individuals die while users are away?
- How much autonomy should a Warden have?
- Can a Warden request guidance, and how often?
- Who may follow private-world entities?
- What social communication exists at launch?
- Are predictions private, public, competitive, or collaborative?
- What makes a question canonical?
- Which events qualify for immediate notification?
- Can an owner pause simulation?
- How should an ancient dormant world re-enter the current season?
- What happens when generated narration conflicts with authoritative state?
- What safeguards apply if children can use Cosmorph?

## Suggested implementation order

1. Chronicle significance scoring and cursor-based change queries.
2. Factual offline recap with before-and-after globe state.
3. Followable species, Wardens, and questions.
4. Structured causal explanations.
5. One meaningful Warden charter revision flow.
6. Open questions and falsifiable forecasts.
7. Chapter recap and permanent legacy.
8. User-controlled digest notifications.
9. Shareable public-world artifacts.
10. Social observation, world discovery, cross-world comparison, and branches only after moderation and scale are ready.

## References

- Ryan, Rigby, and Przybylski, The Motivational Pull of Video Games: https://doi.org/10.1007/s11031-006-9051-8
- Bopp et al., Exploring Emotional Attachment to Game Characters: https://doi.org/10.1145/3311350.3347169
- Yu et al., Exploring Esports Spectator Motivations: https://doi.org/10.1145/3491101.3519652
- Orme, Just Watching: https://doi.org/10.1177/1461444821989350
- Cutting et al., Busy Doing Nothing? What Do Players Do in Idle Games?: https://doi.org/10.1016/j.ijhcs.2018.09.006

