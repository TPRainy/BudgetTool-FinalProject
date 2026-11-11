#ifndef BUDGETCALC_H
#define BUDGETCALC_H

#include <stdint.h>

#if defined(_WIN32)
#define BT_EXPORT __declspec(dllexport)
#define BT_CALL __cdecl
#else
#define BT_EXPORT __attribute__((visibility("default")))
#define BT_CALL
#endif

/* Largest accepted amount: 100 000 000 000 000 cents = 1 000 000 000 000.00 money units. */
#define BT_MAX_CENTS INT64_C(100000000000000)

/* Returned when an argument is outside its valid range. */
#define BT_INVALID_INPUT INT64_C(-1)

#ifdef __cplusplus
extern "C" {
#endif

/*
 * How many percent of a category limit the spent amount uses, rounded half up.
 * Both amounts are in cents (smallest currency unit).
 *   spent_cents: 0 .. BT_MAX_CENTS
 *   limit_cents: 1 .. BT_MAX_CENTS
 * Returns the whole percent (0 or more), or BT_INVALID_INPUT for invalid arguments.
 * Example: spent 40000, limit 30000 -> 133 (133.33 %).
 */
BT_EXPORT int64_t BT_CALL bt_usage_percent(int64_t spent_cents, int64_t limit_cents);

#ifdef __cplusplus
}
#endif

#endif
