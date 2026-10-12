# Frontend UI Development Guidelines

Build the frontend UI using **shadcn/ui** as the primary component library.

## 1. UI/UX Goals

The UI must be:

- Professional and modern
- Clean and minimal
- Easy to understand and navigate
- Consistent across all pages
- Responsive for desktop, tablet, and mobile
- Accessible and keyboard-friendly
- Designed for real-world production use, not a prototype

Avoid unnecessary visual complexity, excessive colors, gradients, animations, or decorative elements.

## 2. shadcn/ui

Use **shadcn/ui components whenever an appropriate component exists**.

Prefer existing components such as:

- Button
- Input
- Select
- Combobox
- Checkbox
- Radio Group
- Switch
- Tabs
- Card
- Dialog
- Sheet
- Dropdown Menu
- Popover
- Tooltip
- Alert
- Badge
- Table
- Pagination
- Breadcrumb
- Calendar
- Form
- Skeleton
- Toast/Alert
- Command

Do not create custom components when an equivalent shadcn/ui component can be used.

If a custom component is required, keep it simple and follow the same visual language as shadcn/ui.

## 3. Layout

Use a consistent application layout:

- Clear sidebar/navigation for major sections
- Clear page title and description
- Consistent spacing
- Logical grouping of related information
- Appropriate use of cards and sections
- Consistent content width
- Clear primary and secondary actions

Example page structure:

```text
Sidebar
 ├── Dashboard
 ├── Members
 ├── Programs
 ├── Transactions
 ├── Configuration
 └── Administration

Main Content
 ├── Breadcrumb
 ├── Page Title
 ├── Page Description
 ├── Primary Actions
 └── Page Content
```

Do not overuse cards. Use cards only when they improve information grouping.

## 4. Visual Design

Follow these principles:

- Use a restrained color palette.
- Use typography to establish hierarchy.
- Use consistent spacing throughout the application.
- Use clear visual hierarchy between primary and secondary actions.
- Use subtle borders and shadows.
- Avoid excessive rounded containers.
- Avoid large empty spaces unless intentional.
- Keep forms compact but comfortable to use.
- Make important actions visually obvious.

The design should feel similar to a high-quality modern SaaS/admin application.

## 5. Forms

Forms must be easy to scan and complete.

Requirements:

- Clear labels
- Helpful descriptions where necessary
- Required-field indicators
- Proper validation
- Inline validation messages
- Appropriate input types
- Logical field grouping
- Disable submit while processing
- Show clear success/error feedback
- Preserve entered values when validation fails

For complex forms, divide fields into logical sections rather than creating one large form.

## 6. Tables and Data

For admin/data-heavy screens:

- Use shadcn/ui Table.
- Provide clear column names.
- Keep columns concise.
- Support sorting/filtering when appropriate.
- Provide pagination for large datasets.
- Provide useful empty states.
- Show loading states using Skeleton.
- Avoid displaying unnecessary information.
- Use badges for statuses where appropriate.
- Put row-specific actions in a dropdown menu when there are multiple actions.

Example:

```text
Members
------------------------------------------------
Search members...       Filter    Add Member

Name          Email          Status      Actions
John Smith    ...            Active      ⋮
Jane Doe      ...            Pending     ⋮

                    < 1 2 3 4 5 >
```

## 7. Loading, Empty, and Error States

Every data-driven page must consider:

### Loading
Use Skeleton components instead of showing a blank page.

### Empty
Provide a useful empty state explaining what the user can do next.

Example:

> No loyalty programs found.
> Create your first program to get started.

Include a relevant primary action.

### Error
Show a clear error message and provide a retry action when appropriate.

Do not expose raw API errors, stack traces, or technical details to users.

## 8. Dialogs and Confirmation

Use Dialog/AlertDialog for actions that require confirmation.

For destructive actions:

- Clearly identify the action
- Explain the consequence
- Use an appropriate destructive button
- Do not make destructive actions the default focused action

Example:

> Delete Loyalty Program?
>
> This action cannot be undone. All configuration associated with this program will be removed.
>
> Cancel | Delete Program

## 9. Responsive Design

The UI must work well on:

- Desktop
- Tablet
- Mobile

Do not simply shrink the desktop UI.

For smaller screens:

- Collapse navigation where appropriate
- Stack form fields
- Make tables usable or provide a responsive alternative
- Keep buttons accessible
- Avoid horizontal overflow

## 10. Accessibility

Follow accessibility best practices:

- Semantic HTML
- Proper labels
- Keyboard navigation
- Visible focus states
- Appropriate ARIA attributes when necessary
- Sufficient color contrast
- Do not rely on color alone to communicate status

## 11. UX Rules

Before implementing each screen, consider:

1. What is the user's primary goal?
2. What is the most important action?
3. What information does the user need first?
4. What actions should be secondary?
5. What happens when there is no data?
6. What happens while data is loading?
7. What happens when an API fails?
8. What happens after the user completes the action?

The UI should minimize unnecessary clicks and make common workflows obvious.

## 12. Component Reuse

Create reusable components for repeated patterns.

Examples:

- PageHeader
- DataTable
- SearchFilter
- StatusBadge
- FormSection
- EmptyState
- LoadingState
- ConfirmDialog
- Pagination
- EntitySelector

Do not duplicate the same UI implementation across multiple pages.

## 13. Implementation Rules

Before writing code:

1. Inspect the existing frontend structure.
2. Identify existing components and design patterns.
3. Reuse existing components whenever possible.
4. Confirm the application already has shadcn/ui configured.
5. Follow the existing project conventions.
6. Do not introduce another UI library unless explicitly requested.
7. Do not rewrite unrelated code.

Before completing the implementation:

- Verify all UI states.
- Verify responsive behavior.
- Verify form validation.
- Verify loading/empty/error states.
- Verify navigation.
- Verify accessibility.
- Remove unnecessary code.
- Ensure there are no console errors.
- Ensure the implementation is production-ready.

## 14. Design Quality Standard

Do not stop at "the page works."

Evaluate the UI as a professional product designer would.

Ask:

- Is the hierarchy obvious?
- Is the primary action obvious?
- Is the page visually balanced?
- Is the workflow intuitive?
- Are there unnecessary elements?
- Are spacing and typography consistent?
- Does it look like a production SaaS application?
- Can a new user understand the page without explanation?

**Prefer simple, consistent, and usable over visually impressive.**

The final result should look like a polished production application built with **shadcn/ui**, not a collection of default components placed on a page.