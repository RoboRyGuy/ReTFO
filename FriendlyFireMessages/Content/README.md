# Friendly Fire Messages

Adds simple friendly fire messages which report who shot who and how much damage they took.

Friendly fire is organized by shooter and shootee. The reports are presented after a short delay; if
another shot between the same shooter/shootee occurs before that delay ends, the delay resets and 
the shot is added to the existing tally.

Sentries are difficult. GTFO does not track if it was a sentry or the sentry's owner which fired
the shot. This mod patches that. If a sentry is owned by someone running this mod, it will be
correctly reported if that sentry shoots someone; otherwise, it will be reported as if the sentry's
owner was the one who shot someone.

Only logs messages locally due to a blend of laziness and technical limitations.