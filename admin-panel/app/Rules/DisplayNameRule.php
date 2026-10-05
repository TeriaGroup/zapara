<?php

namespace App\Rules;

use Closure;
use Illuminate\Contracts\Validation\ValidationRule;

final class DisplayNameRule implements ValidationRule
{
    public function validate(string $attribute, mixed $value, Closure $fail): void
    {
        $message = AccountRules::displayName($value);
        if ($message !== null) {
            $fail($message);
        }
    }
}
