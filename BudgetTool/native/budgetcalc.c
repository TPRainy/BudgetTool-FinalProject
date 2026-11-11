#include "budgetcalc.h"

BT_EXPORT int64_t BT_CALL bt_usage_percent(int64_t spent_cents, int64_t limit_cents)
{
    if (spent_cents < 0 || spent_cents > BT_MAX_CENTS)
        return BT_INVALID_INPUT;

    if (limit_cents <= 0 || limit_cents > BT_MAX_CENTS)
        return BT_INVALID_INPUT;

    /* spent_cents * 100 is at most 10^16, far below the int64_t maximum (about 9.2 * 10^18). */
    return (spent_cents * 100 + limit_cents / 2) / limit_cents;
}
